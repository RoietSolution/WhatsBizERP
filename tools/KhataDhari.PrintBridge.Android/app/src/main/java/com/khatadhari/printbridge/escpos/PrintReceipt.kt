package com.khatadhari.printbridge.escpos

import java.text.Normalizer

data class PrintDocument(
    val version: Int,
    val paperWidth: String,
    val charactersPerLine: Int,
    val operations: List<PrintOperation>,
)

data class PrintOperation(
    val type: String,
    val value: String? = null,
    val alignment: String? = null,
    val bold: Boolean = false,
    val left: String? = null,
    val right: String? = null,
    val count: Int? = null,
)

object PrintDocumentContract {
    const val VERSION = 1
    const val PAPER_WIDTH = "58MM"
    const val CHARACTERS_PER_LINE = 32
    private const val MAX_OPERATIONS = 2_000
    private val alignments = setOf("LEFT", "CENTER", "RIGHT")

    fun validate(document: PrintDocument) {
        require(document.version == VERSION) { "Unsupported print document version ${document.version}. Update KhataDhari Print Bridge." }
        require(document.paperWidth == PAPER_WIDTH && document.charactersPerLine == CHARACTERS_PER_LINE) {
            "This printer does not support the requested paper width."
        }
        require(document.operations.isNotEmpty() && document.operations.size <= MAX_OPERATIONS) {
            "The print document contains an invalid number of operations."
        }
        document.operations.forEachIndexed { index, operation -> validate(operation, index) }
    }

    private fun validate(operation: PrintOperation, index: Int) {
        val prefix = "Malformed print operation ${index + 1}"
        when (operation.type) {
            "TEXT" -> {
                require(operation.value != null && operation.left == null && operation.right == null && operation.count == null) { prefix }
                require((operation.alignment ?: "LEFT") in alignments) { "$prefix: unsupported alignment." }
            }
            "COLUMNS" -> {
                require(operation.value == null && operation.alignment == null && operation.left != null && operation.right != null && operation.count == null) { prefix }
                require(operation.left.isNotBlank() || operation.right.isNotBlank()) { prefix }
            }
            "SEPARATOR" -> {
                require(!operation.bold && operation.alignment == null && operation.left == null && operation.right == null && operation.count == null) { prefix }
                require(operation.value == null || (operation.value.length == 1 && operation.value[0].code in 32..126)) { prefix }
            }
            "FEED" -> {
                require(!operation.bold && operation.value == null && operation.alignment == null && operation.left == null && operation.right == null) { prefix }
                require(operation.count in 1..10) { prefix }
            }
            else -> throw IllegalArgumentException("$prefix: unsupported type '${operation.type}'.")
        }
    }
}

/** Converts only generic layout instructions into native ESC/POS bytes. */
object EscPosDocumentRenderer {
    private const val ESC = '\u001B'

    fun create(document: PrintDocument): ByteArray {
        PrintDocumentContract.validate(document)
        return buildString {
            append(ESC).append('@')
            document.operations.forEach { operation ->
                when (operation.type) {
                    "TEXT" -> {
                        align(operation.alignment ?: "LEFT")
                        emphasize(operation.bold)
                        wrapped(operation.value!!, document.charactersPerLine)
                        emphasize(false)
                    }
                    "COLUMNS" -> {
                        align("LEFT")
                        emphasize(operation.bold)
                        columns(operation.left!!, operation.right!!, document.charactersPerLine)
                        emphasize(false)
                    }
                    "SEPARATOR" -> {
                        align("LEFT")
                        append((operation.value ?: "-").repeat(document.charactersPerLine)).append('\n')
                    }
                    "FEED" -> repeat(operation.count!!) { append('\n') }
                }
            }
            emphasize(false)
            align("LEFT")
        }.toByteArray(Charsets.US_ASCII)
    }

    private fun StringBuilder.align(value: String) {
        val mode = when (value) {
            "CENTER" -> 1
            "RIGHT" -> 2
            else -> 0
        }
        append(ESC).append('a').append(mode.toChar())
    }

    private fun StringBuilder.emphasize(enabled: Boolean) {
        append(ESC).append('E').append(if (enabled) '\u0001' else '\u0000')
    }

    private fun StringBuilder.wrapped(value: String, width: Int) {
        val physicalLines = value.replace("\r\n", "\n").replace('\r', '\n').split('\n')
        physicalLines.forEach { physical ->
            if (physical.isEmpty()) append('\n') else wrap(ascii(physical), width).forEach { append(it).append('\n') }
        }
    }

    private fun StringBuilder.columns(leftValue: String, rightValue: String, width: Int) {
        val left = ascii(leftValue)
        val right = ascii(rightValue)
        if (left.length + right.length + 1 <= width) {
            append(left).append(" ".repeat(width - left.length - right.length)).append(right).append('\n')
            return
        }
        wrapped(left, width)
        align("RIGHT")
        wrapped(right, width)
        align("LEFT")
    }

    private fun wrap(value: String, width: Int): List<String> {
        if (value.isEmpty()) return listOf("")
        val result = mutableListOf<String>()
        var line = ""
        value.trim().split(Regex("\\s+")).forEach { word ->
            var remaining = word
            if (line.isNotEmpty() && line.length + remaining.length + 1 > width) {
                result += line
                line = ""
            }
            while (remaining.length > width) {
                if (line.isNotEmpty()) {
                    result += line
                    line = ""
                }
                result += remaining.take(width)
                remaining = remaining.drop(width)
            }
            if (remaining.isNotEmpty()) line = if (line.isEmpty()) remaining else "$line $remaining"
        }
        if (line.isNotEmpty()) result += line
        return result
    }

    private fun ascii(value: String): String = Normalizer.normalize(value, Normalizer.Form.NFD)
        .replace(Regex("\\p{M}+"), "")
        .map { if (it.code in 32..126) it else '?' }
        .joinToString("")
}

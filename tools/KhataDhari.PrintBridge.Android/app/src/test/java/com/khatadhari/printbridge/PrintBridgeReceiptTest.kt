package com.khatadhari.printbridge

import com.khatadhari.printbridge.escpos.EscPosDocumentRenderer
import com.khatadhari.printbridge.escpos.PrintDocument
import com.khatadhari.printbridge.escpos.PrintDocumentContract
import com.khatadhari.printbridge.escpos.PrintOperation
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class PrintBridgeReceiptTest {
    @Test
    fun textOperationPrintsProvidedText() {
        val output = render(PrintOperation("TEXT", value = "Server supplied text"))

        assertTrue(output.contains("Server supplied text\n"))
    }

    @Test
    fun centerAlignmentEmitsEscPosCenterCommand() {
        val bytes = bytes(PrintOperation("TEXT", value = "Centered", alignment = "CENTER"))

        assertTrue(bytes.containsSequence(byteArrayOf(0x1B, 0x61, 0x01)))
    }

    @Test
    fun rightAlignmentEmitsEscPosRightCommand() {
        val bytes = bytes(PrintOperation("TEXT", value = "Right", alignment = "RIGHT"))

        assertTrue(bytes.containsSequence(byteArrayOf(0x1B, 0x61, 0x02)))
    }

    @Test
    fun boldTextEmitsEmphasisAroundProvidedText() {
        val output = render(PrintOperation("TEXT", value = "Important", bold = true))

        assertTrue(output.contains("\u001BE\u0001Important\n\u001BE\u0000"))
    }

    @Test
    fun columnsUseTheDeclaredLineWidth() {
        val output = render(PrintOperation("COLUMNS", left = "Left", right = "Right"))

        assertTrue(output.contains("Left" + " ".repeat(23) + "Right\n"))
    }

    @Test
    fun separatorUsesTheDeclaredLineWidth() {
        val output = render(PrintOperation("SEPARATOR", value = "-"))

        assertTrue(output.contains("-".repeat(PrintDocumentContract.CHARACTERS_PER_LINE) + "\n"))
    }

    @Test
    fun longTextWrapsWithoutEmbeddingReceiptKnowledge() {
        val output = render(PrintOperation("TEXT", value = "12345678901234567890123456789012 next"))

        assertTrue(output.contains("12345678901234567890123456789012\nnext\n"))
    }

    @Test
    fun feedOperationAdvancesTheRequestedLines() {
        val output = render(PrintOperation("FEED", count = 3))

        assertTrue(output.contains("\n\n\n"))
    }

    @Test
    fun unsupportedContractVersionIsRejectedSafely() {
        val failure = runCatching {
            EscPosDocumentRenderer.create(document(PrintOperation("TEXT", value = "x"), version = 2))
        }.exceptionOrNull()

        assertTrue(failure is IllegalArgumentException)
        assertTrue(failure?.message?.contains("Unsupported print document version 2") == true)
    }

    @Test
    fun malformedOperationIsRejectedSafely() {
        val failure = runCatching {
            EscPosDocumentRenderer.create(document(PrintOperation("COLUMNS", left = "missing right")))
        }.exceptionOrNull()

        assertTrue(failure is IllegalArgumentException)
        assertTrue(failure?.message?.contains("Malformed print operation 1") == true)
    }

    @Test
    fun emptyReferenceIsRejectedBeforeNetworkOrUriAccess() {
        val failure = runCatching { PrintBridgeApi.fetch(null, null) }.exceptionOrNull()
        assertTrue(failure is IllegalArgumentException)
        assertEquals("The print request is missing or malformed.", failure?.message)
    }

    private fun render(operation: PrintOperation): String = bytes(operation).toString(Charsets.US_ASCII)

    private fun bytes(operation: PrintOperation): ByteArray =
        EscPosDocumentRenderer.create(document(operation))

    private fun document(operation: PrintOperation, version: Int = PrintDocumentContract.VERSION) =
        PrintDocument(version, PrintDocumentContract.PAPER_WIDTH, PrintDocumentContract.CHARACTERS_PER_LINE, listOf(operation))

    private fun ByteArray.containsSequence(sequence: ByteArray): Boolean =
        indices.any { start -> start + sequence.size <= size && sequence.indices.all { this[start + it] == sequence[it] } }
}

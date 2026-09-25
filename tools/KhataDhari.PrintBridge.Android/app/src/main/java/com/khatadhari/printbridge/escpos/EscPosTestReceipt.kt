package com.khatadhari.printbridge.escpos

import java.io.ByteArrayOutputStream
import java.nio.charset.StandardCharsets

/** Test-only ASCII receipt. No invoice or tax calculations belong here. */
object EscPosTestReceipt {
    fun create(): ByteArray {
        val output = ByteArrayOutputStream()
        fun bytes(vararg values: Int) = output.write(values.map { it.toByte() }.toByteArray())
        fun text(value: String) = output.write(value.toByteArray(StandardCharsets.US_ASCII))

        bytes(0x1B, 0x40) // ESC @ — initialize printer
        bytes(0x1B, 0x61, 0x01) // center
        text("          KHATADHARI\n")
        text("--------------------------------\n")
        text("      MT580P PRINT TEST\n\n")
        bytes(0x1B, 0x61, 0x00) // left
        text("Android Bluetooth: OK\n")
        text("ESC/POS: OK\n\n")
        text("Printer: MT580P\n")
        text("Paper: 58mm\n")
        text("--------------------------------\n")
        bytes(0x1B, 0x61, 0x01) // center
        text("      Print Successful\n\n\n\n")
        bytes(0x1B, 0x61, 0x00) // restore left alignment
        return output.toByteArray()
    }
}

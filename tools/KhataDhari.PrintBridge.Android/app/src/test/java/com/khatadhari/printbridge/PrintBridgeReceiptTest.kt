package com.khatadhari.printbridge

import com.khatadhari.printbridge.escpos.EscPosInvoiceReceipt
import com.khatadhari.printbridge.escpos.PrintBusiness
import com.khatadhari.printbridge.escpos.PrintInvoice
import com.khatadhari.printbridge.escpos.PrintItem
import com.khatadhari.printbridge.escpos.PrintPayment
import com.khatadhari.printbridge.escpos.PrintReceiptRequest
import com.khatadhari.printbridge.escpos.PrintTax
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.math.BigDecimal

class PrintBridgeReceiptTest {
    @Test
    fun finalizedValuesAppearInAsciiReceiptWithoutRecalculation() {
        val invoice = PrintInvoice(
            number = "INV-TEST-42",
            date = "2026-09-25T10:30:00+05:30",
            status = "COMPLETED",
            customer = "Test Customer",
            customerGstin = "29ABCDE1234F1Z5",
            subtotal = BigDecimal("250.00"),
            discount = BigDecimal("10.00"),
            tax = BigDecimal("43.20"),
            roundOff = BigDecimal("0.20"),
            total = BigDecimal("283.40"),
            paid = BigDecimal("283.40"),
            balance = BigDecimal("0.00"),
            items = listOf(PrintItem("Milk", BigDecimal("2"), "L", BigDecimal("125.00"), BigDecimal("10.00"), BigDecimal("5"), BigDecimal("11.50"), BigDecimal("251.50"))),
            taxes = listOf(PrintTax("CGST", BigDecimal("2.5"), BigDecimal("230.00"), BigDecimal("5.75")), PrintTax("SGST", BigDecimal("2.5"), BigDecimal("230.00"), BigDecimal("5.75"))),
            payments = listOf(PrintPayment("Cash", BigDecimal("283.40"), "COMPLETED")),
        )
        val receipt = EscPosInvoiceReceipt.create(PrintReceiptRequest(PrintBusiness("Test Store", "Test Address", "29ABCDE1234F1Z5", "9876543210"), invoice))
        val text = receipt.toString(Charsets.US_ASCII)

        assertTrue(text.startsWith("\u001B@"))
        assertTrue(text.contains("INV-TEST-42"))
        assertTrue(text.contains("CGST 2.5%"))
        assertTrue(text.contains("Rs. 283.40"))
        assertTrue(text.contains("Powered by KhataDhari"))
        assertTrue(text.all { it.code < 128 })
    }

    @Test
    fun emptyReferenceIsRejectedBeforeNetworkOrUriAccess() {
        val failure = runCatching { PrintBridgeApi.fetch(null, null) }.exceptionOrNull()
        assertTrue(failure is IllegalArgumentException)
        assertEquals("The print request is missing or malformed.", failure?.message)
    }
}

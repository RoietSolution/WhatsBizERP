package com.khatadhari.printbridge

import com.khatadhari.printbridge.escpos.EscPosInvoiceReceipt
import com.khatadhari.printbridge.escpos.PrintBusiness
import com.khatadhari.printbridge.escpos.PrintInvoice
import com.khatadhari.printbridge.escpos.PrintItem
import com.khatadhari.printbridge.escpos.PrintPayment
import com.khatadhari.printbridge.escpos.PrintReceiptRequest
import com.khatadhari.printbridge.escpos.PrintTax
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.math.BigDecimal

class PrintBridgeReceiptTest {
    @Test
    fun completedReceiptFollowsWhatsBiz58mmLogicalSectionOrder() {
        val text = receipt("COMPLETED")

        assertInOrder(text,
            "Test Store", "Test Store Private Limited", "GSTIN: 29ABCDE1234F1Z5",
            "GST INVOICE", "Bill No: INV-TEST-42", "Date", "25-Sep-2026", "Time", "10:30",
            "Counter", "abc123", "Cashier", "cashier", "Payment", "Cash",
            "Customer", "Test Customer", "Receipt No", "Milk", "Qty 2", "Rate Rs. 125.00",
            "GST% 5", "GST Rs. 11.50", "Amount", "Rs. 251.50",
            "Subtotal", "Rs. 250.00", "Discount", "Rs. 10.00",
            "Taxable Amount", "Rs. 240.00", "Total GST", "Rs. 43.20",
            "Grand Total", "Rs. 283.40", "Paid", "Balance", "Total Amount",
            "Returns accepted within seven", "days.", "Thank you from Test Store.",
        )
        assertFalse(text.contains("PROVISIONAL BILL"))
        assertFalse(text.contains("NOT A GST TAX INVOICE"))
        assertTrue(text.startsWith("\u001B@"))
        assertTrue(text.all { it.code < 128 })
    }

    @Test
    fun heldReceiptIsClearlyProvisionalAndNeverIdentifiesAsFinalGstInvoice() {
        val text = receipt("HELD")

        assertTrue(text.contains("PROVISIONAL BILL"))
        assertTrue(text.contains("Status: HELD"))
        assertTrue(text.contains("NOT A GST TAX INVOICE"))
        assertFalse(text.lines().any { it.trim() == "GST INVOICE" })
        assertTrue(text.contains("Taxable Amount"))
        assertTrue(text.contains("Total GST"))
    }

    @Test
    fun suspendedReceiptUsesTheSameProvisionalProtection() {
        val text = receipt("SUSPENDED")

        assertTrue(text.contains("PROVISIONAL BILL"))
        assertTrue(text.contains("Status: SUSPENDED"))
        assertTrue(text.contains("NOT A GST TAX INVOICE"))
        assertFalse(text.lines().any { it.trim() == "GST INVOICE" })
    }

    @Test
    fun formatterPrintsServerProvidedTaxableValueWithoutDerivingIt() {
        val text = receipt("COMPLETED", taxable = BigDecimal("77.77"))

        assertInOrder(text, "Taxable Amount", "Rs. 77.77", "Total GST")
        assertFalse(text.contains("Rs. 240.00"))
    }

    @Test
    fun emptyReferenceIsRejectedBeforeNetworkOrUriAccess() {
        val failure = runCatching { PrintBridgeApi.fetch(null, null) }.exceptionOrNull()
        assertTrue(failure is IllegalArgumentException)
        assertEquals("The print request is missing or malformed.", failure?.message)
    }

    private fun receipt(status: String, taxable: BigDecimal = BigDecimal("240.00")): String {
        val invoice = PrintInvoice(
            number = "INV-TEST-42",
            date = "2026-09-25T10:30:00+05:30",
            status = status,
            customer = "Test Customer",
            customerGstin = "29ABCDE1234F1Z5",
            counter = "abc123",
            cashier = "cashier",
            subtotal = BigDecimal("250.00"),
            discount = BigDecimal("10.00"),
            taxable = taxable,
            tax = BigDecimal("43.20"),
            roundOff = BigDecimal("0.20"),
            total = BigDecimal("283.40"),
            paid = if (status == "HELD" || status == "SUSPENDED") BigDecimal.ZERO else BigDecimal("283.40"),
            balance = if (status == "HELD" || status == "SUSPENDED") BigDecimal("283.40") else BigDecimal("0.00"),
            items = listOf(PrintItem("Milk", BigDecimal("2"), "L", BigDecimal("125.00"), BigDecimal("10.00"), BigDecimal("5"), BigDecimal("11.50"), BigDecimal("251.50"))),
            taxes = listOf(PrintTax("CGST", BigDecimal("2.5"), BigDecimal("230.00"), BigDecimal("5.75")), PrintTax("SGST", BigDecimal("2.5"), BigDecimal("230.00"), BigDecimal("5.75"))),
            payments = if (status == "HELD" || status == "SUSPENDED") emptyList() else listOf(PrintPayment("Cash", BigDecimal("283.40"), "COMPLETED")),
        )
        return EscPosInvoiceReceipt.create(PrintReceiptRequest(
            PrintBusiness("Test Store", "Test Store Private Limited", "Test Address", "29ABCDE1234F1Z5", "9876543210", "billing@test.example", "Returns accepted within seven days.", "Thank you from Test Store."),
            invoice,
        )).toString(Charsets.US_ASCII)
    }

    private fun assertInOrder(text: String, vararg values: String) {
        var position = -1
        values.forEach { value ->
            val next = text.indexOf(value, position + 1)
            assertTrue("Expected '$value' after position $position in receipt:\n$text", next > position)
            position = next
        }
    }
}

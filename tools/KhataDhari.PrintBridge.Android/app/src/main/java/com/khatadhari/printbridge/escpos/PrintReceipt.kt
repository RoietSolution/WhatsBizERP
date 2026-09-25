package com.khatadhari.printbridge.escpos

import java.math.BigDecimal
import java.text.Normalizer

data class PrintBusiness(
    val name: String,
    val legalName: String?,
    val address: String,
    val gstin: String?,
    val phone: String?,
    val email: String?,
    val termsAndConditions: String?,
    val invoiceFooter: String?,
)
data class PrintItem(val name: String, val quantity: BigDecimal, val unit: String?, val rate: BigDecimal, val discount: BigDecimal, val taxRate: BigDecimal, val tax: BigDecimal, val amount: BigDecimal)
data class PrintTax(val type: String, val rate: BigDecimal, val taxable: BigDecimal, val amount: BigDecimal)
data class PrintPayment(val method: String, val amount: BigDecimal, val status: String)
data class PrintInvoice(
    val number: String,
    val date: String,
    val status: String,
    val customer: String?,
    val customerGstin: String?,
    val counter: String?,
    val cashier: String?,
    val subtotal: BigDecimal,
    val discount: BigDecimal,
    val taxable: BigDecimal,
    val tax: BigDecimal,
    val roundOff: BigDecimal,
    val total: BigDecimal,
    val paid: BigDecimal,
    val balance: BigDecimal,
    val items: List<PrintItem>,
    val taxes: List<PrintTax>,
    val payments: List<PrintPayment>,
)
data class PrintReceiptRequest(val business: PrintBusiness, val invoice: PrintInvoice)

/**
 * Native ESC/POS rendering of the production WhatsBiz 58mm receipt's content and section order.
 * Every accounting value is supplied by the ERP; this formatter performs no amount or tax calculation.
 */
object EscPosInvoiceReceipt {
    const val LINE_WIDTH = 32
    private const val ESC = '\u001B'

    fun create(receipt: PrintReceiptRequest): ByteArray = buildString {
        append(ESC).append('@')
        align(1)
        emphasize(true)
        wrapped(receipt.business.name, centered = true)
        emphasize(false)
        receipt.business.legalName?.takeIf(String::isNotBlank)?.let { wrapped(it, centered = true) }
        receipt.business.address.takeIf(String::isNotBlank)?.let { wrapped(it, centered = true) }
        receipt.business.gstin?.takeIf(String::isNotBlank)?.let { wrapped("GSTIN: $it", centered = true) }
        val contact = listOfNotNull(
            receipt.business.phone?.takeIf(String::isNotBlank),
            receipt.business.email?.takeIf(String::isNotBlank),
        ).joinToString(" / ")
        contact.takeIf(String::isNotBlank)?.let { wrapped(it, centered = true) }

        align(0)
        rule()
        align(1)
        emphasize(true)
        if (receipt.invoice.status in setOf("HELD", "SUSPENDED")) {
            centered("PROVISIONAL BILL")
            emphasize(false)
            centered("Status: ${receipt.invoice.status}")
            emphasize(true)
            centered("NOT A GST TAX INVOICE")
        } else {
            centered("GST INVOICE")
        }
        emphasize(false)
        centered("Bill No: ${receipt.invoice.number}")

        align(0)
        rule()
        labelValue("Date", displayDate(receipt.invoice.date))
        labelValue("Time", displayTime(receipt.invoice.date))
        receipt.invoice.counter?.takeIf(String::isNotBlank)?.let { labelValue("Counter", it) }
        receipt.invoice.cashier?.takeIf(String::isNotBlank)?.let { labelValue("Cashier", it) }
        labelValue("Payment", receipt.invoice.payments.joinToString(", ") { it.method }.ifBlank { "Unpaid" })
        labelValue("Customer", receipt.invoice.customer?.takeIf(String::isNotBlank) ?: "Walk-in")
        labelValue("Receipt No", receipt.invoice.number)

        rule()
        receipt.invoice.items.forEach { item ->
            emphasize(true)
            wrapped(item.name)
            emphasize(false)
            line("Qty ${quantity(item.quantity)}", "Rate ${money(item.rate)}")
            line("GST% ${quantity(item.taxRate)}", "GST ${money(item.tax)}")
            emphasize(true)
            line("Amount", money(item.amount))
            emphasize(false)
            rule()
        }

        line("Subtotal", money(receipt.invoice.subtotal))
        line("Discount", money(receipt.invoice.discount))
        line("Taxable Amount", money(receipt.invoice.taxable))
        line("Total GST", money(receipt.invoice.tax))
        rule()
        emphasize(true)
        line("Grand Total", money(receipt.invoice.total))
        emphasize(false)
        rule()
        line("Paid", money(receipt.invoice.paid))
        line("Balance", money(receipt.invoice.balance))
        rule()
        emphasize(true)
        line("Total Amount", money(receipt.invoice.total))
        emphasize(false)

        rule()
        receipt.business.termsAndConditions?.takeIf(String::isNotBlank)?.let { wrapped(it, centered = true) }
        align(1)
        emphasize(true)
        wrapped(receipt.business.invoiceFooter?.takeIf(String::isNotBlank) ?: "Thank you for shopping with us!", centered = true)
        emphasize(false)
        append("\n\n\n")
        align(0)
    }.toByteArray(Charsets.US_ASCII)

    private fun StringBuilder.align(value: Int) {
        append(ESC).append('a').append(value.toChar())
    }

    private fun StringBuilder.emphasize(enabled: Boolean) {
        append(ESC).append('E').append(if (enabled) '\u0001' else '\u0000')
    }

    private fun StringBuilder.line(left: String, right: String? = null) {
        val clean = ascii(left)
        if (right == null) append(clean.take(LINE_WIDTH)).append('\n')
        else {
            val value = ascii(right).take(LINE_WIDTH)
            append(clean.take((LINE_WIDTH - value.length - 1).coerceAtLeast(0)).padEnd((LINE_WIDTH - value.length).coerceAtLeast(1)))
            append(value).append('\n')
        }
    }

    private fun StringBuilder.labelValue(label: String, value: String) {
        val cleanLabel = ascii(label)
        val cleanValue = ascii(value)
        if (cleanLabel.length + cleanValue.length + 1 <= LINE_WIDTH) {
            line(cleanLabel, cleanValue)
            return
        }
        line(cleanLabel)
        wrap(cleanValue).forEach { line("", it) }
    }

    private fun StringBuilder.wrapped(value: String, centered: Boolean = false) {
        wrap(ascii(value)).forEach { if (centered) centered(it) else line(it) }
    }

    private fun StringBuilder.rule() {
        append("-".repeat(LINE_WIDTH)).append('\n')
    }

    private fun StringBuilder.centered(value: String) {
        append(center(value)).append('\n')
    }

    private fun wrap(value: String): List<String> {
        if (value.isBlank()) return emptyList()
        val result = mutableListOf<String>()
        var line = ""
        value.trim().split(Regex("\\s+")).forEach { word ->
            var remaining = word
            if (line.isNotEmpty() && line.length + remaining.length + 1 > LINE_WIDTH) {
                result += line
                line = ""
            }
            while (remaining.length > LINE_WIDTH) {
                if (line.isNotEmpty()) {
                    result += line
                    line = ""
                }
                result += remaining.take(LINE_WIDTH)
                remaining = remaining.drop(LINE_WIDTH)
            }
            if (remaining.isNotEmpty()) line = if (line.isEmpty()) remaining else "$line $remaining"
        }
        if (line.isNotEmpty()) result += line
        return result
    }

    private fun center(value: String): String {
        val text = ascii(value).take(LINE_WIDTH)
        return text.padStart((LINE_WIDTH + text.length) / 2).padEnd(LINE_WIDTH)
    }

    private fun money(value: BigDecimal) = "Rs. " + value.toPlainString()
    private fun quantity(value: BigDecimal) = value.stripTrailingZeros().toPlainString()

    private fun displayDate(value: String): String {
        val date = value.take(10)
        if (!Regex("\\d{4}-\\d{2}-\\d{2}").matches(date)) return value
        val months = arrayOf("Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec")
        val month = date.substring(5, 7).toIntOrNull()?.takeIf { it in 1..12 } ?: return date
        return "${date.substring(8, 10)}-${months[month - 1]}-${date.substring(0, 4)}"
    }

    private fun displayTime(value: String): String =
        value.substringAfter('T', "").take(5).takeIf { Regex("\\d{2}:\\d{2}").matches(it) } ?: ""

    private fun ascii(value: String): String = Normalizer.normalize(value, Normalizer.Form.NFD)
        .replace(Regex("\\p{M}+"), "")
        .map { if (it.code in 32..126) it else '?' }
        .joinToString("")
}

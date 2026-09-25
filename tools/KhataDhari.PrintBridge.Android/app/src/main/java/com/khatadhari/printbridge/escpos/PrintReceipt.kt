package com.khatadhari.printbridge.escpos

import java.math.BigDecimal
import java.text.Normalizer

data class PrintBusiness(val name: String, val address: String, val gstin: String?, val phone: String?)
data class PrintItem(val name: String, val quantity: BigDecimal, val unit: String?, val rate: BigDecimal, val discount: BigDecimal, val taxRate: BigDecimal, val tax: BigDecimal, val amount: BigDecimal)
data class PrintTax(val type: String, val rate: BigDecimal, val taxable: BigDecimal, val amount: BigDecimal)
data class PrintPayment(val method: String, val amount: BigDecimal, val status: String)
data class PrintInvoice(val number: String, val date: String, val status: String, val customer: String?, val customerGstin: String?, val subtotal: BigDecimal, val discount: BigDecimal, val tax: BigDecimal, val roundOff: BigDecimal, val total: BigDecimal, val paid: BigDecimal, val balance: BigDecimal, val items: List<PrintItem>, val taxes: List<PrintTax>, val payments: List<PrintPayment>)
data class PrintReceiptRequest(val business: PrintBusiness, val invoice: PrintInvoice)

/** Formats server-finalized invoice values only. No amount or tax calculation is performed here. */
object EscPosInvoiceReceipt {
    const val LINE_WIDTH = 32

    fun create(receipt: PrintReceiptRequest): ByteArray = buildString {
        append("\u001B@")
        append(center(receipt.business.name)); append('\n')
        line(receipt.business.address)
        receipt.business.phone?.takeIf(String::isNotBlank)?.let { line(it) }
        receipt.business.gstin?.takeIf(String::isNotBlank)?.let { line("GSTIN: $it") }
        rule(); centerLine("GST INVOICE"); line("Invoice: ${receipt.invoice.number}")
        line("Date: ${receipt.invoice.date}")
        receipt.invoice.customer?.takeIf(String::isNotBlank)?.let { line("Customer: $it") }
        receipt.invoice.customerGstin?.takeIf(String::isNotBlank)?.let { line("Customer GSTIN: $it") }
        rule()
        receipt.invoice.items.forEach { item ->
            line(item.name + (item.unit?.takeIf(String::isNotBlank)?.let { " ($it)" } ?: ""))
            line("${quantity(item.quantity)} x ${money(item.rate)}".take(LINE_WIDTH - 11), money(item.amount))
            if (item.discount > BigDecimal.ZERO) line("Item discount", money(item.discount))
            if (item.taxRate > BigDecimal.ZERO || item.tax > BigDecimal.ZERO) line("GST ${item.taxRate.stripTrailingZeros().toPlainString()}%", money(item.tax))
        }
        rule()
        line("Subtotal", money(receipt.invoice.subtotal))
        line("Discount", money(receipt.invoice.discount))
        receipt.invoice.taxes.forEach { tax ->
            line("${tax.type} taxable", money(tax.taxable))
            line("${tax.type} ${tax.rate.stripTrailingZeros().toPlainString()}%", money(tax.amount))
        }
        if (receipt.invoice.taxes.isEmpty()) line("Tax", money(receipt.invoice.tax))
        line("Round Off", money(receipt.invoice.roundOff))
        rule(); line("TOTAL", money(receipt.invoice.total)); rule()
        if (receipt.invoice.payments.isEmpty()) line("Payment: Unpaid") else receipt.invoice.payments.forEach { line("${it.method} (${it.status})", money(it.amount)) }
        line("Paid", money(receipt.invoice.paid)); line("Balance", money(receipt.invoice.balance))
        rule(); centerLine("Thank You"); centerLine("Powered by KhataDhari")
        append("\n\n\n")
    }.toByteArray(Charsets.US_ASCII)

    private fun StringBuilder.line(left: String, right: String? = null) {
        val clean = ascii(left)
        if (right == null) append(clean.take(LINE_WIDTH)).append('\n')
        else {
            val value = ascii(right).take(LINE_WIDTH)
            append(clean.take((LINE_WIDTH - value.length - 1).coerceAtLeast(0)).padEnd((LINE_WIDTH - value.length).coerceAtLeast(1)))
            append(value).append('\n')
        }
    }
    private fun StringBuilder.rule() { append("-".repeat(LINE_WIDTH)).append('\n') }
    private fun StringBuilder.centerLine(value: String) { append(center(value)).append('\n') }
    private fun center(value: String): String { val text = ascii(value).take(LINE_WIDTH); return text.padStart((LINE_WIDTH + text.length) / 2).padEnd(LINE_WIDTH) }
    private fun money(value: BigDecimal) = "Rs. " + value.toPlainString()
    private fun quantity(value: BigDecimal) = value.stripTrailingZeros().toPlainString()
    private fun ascii(value: String): String = Normalizer.normalize(value, Normalizer.Form.NFD).replace(Regex("\\p{M}+"), "").map { if (it.code in 32..126) it else '?' }.joinToString("")
}

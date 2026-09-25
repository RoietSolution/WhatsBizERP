package com.khatadhari.printbridge

import android.net.Uri
import com.khatadhari.printbridge.escpos.PrintBusiness
import com.khatadhari.printbridge.escpos.PrintInvoice
import com.khatadhari.printbridge.escpos.PrintItem
import com.khatadhari.printbridge.escpos.PrintPayment
import com.khatadhari.printbridge.escpos.PrintReceiptRequest
import com.khatadhari.printbridge.escpos.PrintTax
import org.json.JSONArray
import org.json.JSONObject
import java.math.BigDecimal
import java.net.HttpURLConnection
import java.net.URL

object PrintBridgeApi {
    private val allowedHosts = setOf("api.khatadhari.com", "qa-api.khatadhari.com")

    fun fetch(apiOriginValue: String?, reference: String?): PrintReceiptRequest {
        require(!reference.isNullOrBlank() && reference.length <= 2048) { "The print request is missing or malformed." }
        val origin = Uri.parse(apiOriginValue)
        require(origin.scheme == "https" && origin.host in allowedHosts && origin.userInfo == null &&
            (origin.port == -1 || origin.port == 443) && (origin.path.isNullOrEmpty() || origin.path == "/")) {
            "This WhatsBiz API address is not supported. Open Print Receipt from the signed-in POS."
        }
        val endpoint = URL("https://${origin.host}/api/pos/print-bridge/receipt")
        val connection = (endpoint.openConnection() as HttpURLConnection).apply {
            requestMethod = "POST"
            connectTimeout = 10_000
            readTimeout = 15_000
            setRequestProperty("Accept", "application/json")
            setRequestProperty("Content-Type", "application/json; charset=utf-8")
            doOutput = true
            useCaches = false
        }
        try {
            connection.outputStream.use { it.write(JSONObject().put("reference", reference).toString().toByteArray(Charsets.UTF_8)) }
            if (connection.responseCode != 200) throw IllegalStateException(
                when (connection.responseCode) {
                    404, 410 -> "The print request expired or is invalid. Return to POS and tap Print Receipt again."
                    401, 403 -> "The invoice is unavailable for this retailer account."
                    else -> "WhatsBiz could not retrieve the invoice. Check the network and try again."
                })
            return parse(JSONObject(connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }))
        } finally { connection.disconnect() }
    }

    fun parse(root: JSONObject): PrintReceiptRequest {
        val businessJson = root.getJSONObject("business")
        val invoiceJson = root.getJSONObject("invoice")
        val itemsJson = invoiceJson.getJSONArray("items")
        require(invoiceJson.getString("number").isNotBlank() && itemsJson.length() > 0) { "The print request does not contain a printable saved invoice." }
        val business = PrintBusiness(
            businessJson.getString("name"),
            businessJson.optNullableString("legalName"),
            businessJson.optString("address", ""),
            businessJson.optNullableString("gstin"),
            businessJson.optNullableString("phone"),
            businessJson.optNullableString("email"),
            businessJson.optNullableString("termsAndConditions"),
            businessJson.optNullableString("invoiceFooter"),
        )
        val items = itemsJson.mapObjects { x -> PrintItem(x.getString("name"), x.decimal("quantity"), x.optNullableString("unit"), x.decimal("rate"), x.decimal("discount"), x.decimal("taxPercentage"), x.decimal("taxAmount"), x.decimal("amount")) }
        val taxes = invoiceJson.optJSONArray("taxes").mapObjects { x -> PrintTax(x.getString("type"), x.decimal("rate"), x.decimal("taxableAmount"), x.decimal("amount")) }
        val payments = invoiceJson.optJSONArray("payments").mapObjects { x -> PrintPayment(x.getString("method"), x.decimal("amount"), x.getString("status")) }
        val invoice = PrintInvoice(
            invoiceJson.getString("number"), invoiceJson.getString("date"), invoiceJson.getString("status"),
            invoiceJson.optNullableString("customerName"), invoiceJson.optNullableString("customerGstin"),
            invoiceJson.optNullableString("counter"), invoiceJson.optNullableString("cashier"),
            invoiceJson.decimal("subtotal"), invoiceJson.decimal("discount"), invoiceJson.decimal("taxableAmount"),
            invoiceJson.decimal("tax"), invoiceJson.decimal("roundOff"), invoiceJson.decimal("grandTotal"),
            invoiceJson.decimal("paid"), invoiceJson.decimal("balance"), items, taxes, payments,
        )
        return PrintReceiptRequest(business, invoice)
    }

    private fun JSONObject.decimal(name: String): BigDecimal = BigDecimal(get(name).toString())
    private fun JSONObject.optNullableString(name: String): String? = if (isNull(name) || !has(name)) null else optString(name).takeIf(String::isNotBlank)
    private inline fun <T> JSONArray?.mapObjects(mapper: (JSONObject) -> T): List<T> {
        if (this == null) return emptyList()
        return (0 until length()).map { mapper(getJSONObject(it)) }
    }
}

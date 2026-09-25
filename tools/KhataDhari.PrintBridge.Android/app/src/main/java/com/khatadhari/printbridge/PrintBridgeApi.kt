package com.khatadhari.printbridge

import android.net.Uri
import com.khatadhari.printbridge.escpos.PrintDocument
import com.khatadhari.printbridge.escpos.PrintDocumentContract
import com.khatadhari.printbridge.escpos.PrintOperation
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

object PrintBridgeApi {
    private val allowedHosts = setOf("api.khatadhari.com", "qa-api.khatadhari.com")

    fun fetch(apiOriginValue: String?, reference: String?): PrintDocument {
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
                    401, 403 -> "The print document is unavailable for this retailer account."
                    else -> "WhatsBiz could not retrieve the print document. Check the network and try again."
                })
            return parse(JSONObject(connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }))
        } finally { connection.disconnect() }
    }

    fun parse(root: JSONObject): PrintDocument {
        val operationsJson = root.getJSONArray("operations")
        val operations = (0 until operationsJson.length()).map { index ->
            val operation = operationsJson.getJSONObject(index)
            PrintOperation(
                type = operation.getString("type"),
                value = operation.nullableString("value"),
                alignment = operation.nullableString("alignment"),
                bold = if (operation.has("bold")) operation.getBoolean("bold") else false,
                left = operation.nullableString("left"),
                right = operation.nullableString("right"),
                count = if (operation.has("count") && !operation.isNull("count")) operation.getInt("count") else null,
            )
        }
        return PrintDocument(
            root.getInt("version"),
            root.getString("paperWidth"),
            root.getInt("charactersPerLine"),
            operations,
        ).also(PrintDocumentContract::validate)
    }

    private fun JSONObject.nullableString(name: String): String? =
        if (!has(name) || isNull(name)) null else getString(name)
}

package com.khatadhari.printbridge

import android.Manifest
import android.annotation.SuppressLint
import android.app.Activity
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothSocket
import android.content.Context
import android.content.pm.PackageManager
import android.graphics.Typeface
import android.os.Build
import android.os.Bundle
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.view.Gravity
import android.view.ViewGroup
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import android.view.View
import com.khatadhari.printbridge.bluetooth.ClassicBluetoothTransport
import com.khatadhari.printbridge.escpos.EscPosTestReceipt
import com.khatadhari.printbridge.escpos.EscPosInvoiceReceipt
import com.khatadhari.printbridge.escpos.PrintReceiptRequest
import java.io.IOException
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.concurrent.Executors

class MainActivity : Activity() {
    companion object {
        private const val REQUEST_BLUETOOTH_CONNECT = 4101
        private const val CONNECT_TIMEOUT_MS = 25_000L
        private const val LOG_LINE_LIMIT = 160
        private const val PRINTER_NAME = "MT580P"
    }

    private val mainHandler = Handler(Looper.getMainLooper())
    private val ioExecutor = Executors.newSingleThreadExecutor()
    private val transport = ClassicBluetoothTransport()
    private val pairedDevices = mutableListOf<BluetoothDevice>()

    private var bluetoothAdapter: BluetoothAdapter? = null
    @Volatile private var pendingSocket: BluetoothSocket? = null
    @Volatile private var connectedSocket: BluetoothSocket? = null
    @Volatile private var timedOutSocket: BluetoothSocket? = null
    private var connecting = false
    private var printing = false
    private var connectTimeoutTask: Runnable? = null

    private lateinit var statusView: TextView
    private lateinit var deviceSpinner: Spinner
    private lateinit var spinnerAdapter: ArrayAdapter<String>
    private lateinit var refreshButton: Button
    private lateinit var connectButton: Button
    private lateinit var printButton: Button
    private lateinit var receiptPrintButton: Button
    private lateinit var receiptPreview: TextView
    private lateinit var disconnectButton: Button
    private lateinit var logView: TextView
    private lateinit var logScroll: ScrollView
    private val logLines = mutableListOf<String>()
    private var printRequest: PrintReceiptRequest? = null
    private var printRequestGeneration = 0
    private val printerPreferences by lazy { getSharedPreferences("printer-preferences", MODE_PRIVATE) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        bluetoothAdapter = (getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager)?.adapter
        buildScreen()
        updateControls()
        appendLog("KhataDhari Print Bridge ready. Pair the MT580P in Android Settings, then refresh paired devices.")
        appendLog("Transport: Bluetooth Classic RFCOMM/SPP. BLE GATT is not used by this POC.")
        handlePrintIntent(intent)
    }

    private fun buildScreen() {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(18), dp(22), dp(18), dp(14))
            setBackgroundColor(0xFFF4F7F5.toInt())
        }

        val title = TextView(this).apply {
            text = "KhataDhari Print Bridge"
            textSize = 25f
            typeface = Typeface.create("sans-serif", Typeface.BOLD)
            setTextColor(0xFF105239.toInt())
        }
        root.addView(title, matchWrap())

        val subtitle = TextView(this).apply {
            text = "MT580P · 58mm · ESC/POS hardware test"
            textSize = 14f
            setTextColor(0xFF637168.toInt())
            setPadding(0, dp(5), 0, dp(16))
        }
        root.addView(subtitle, matchWrap())

        statusView = TextView(this).apply {
            text = "Connection: Disconnected"
            textSize = 17f
            typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
            setTextColor(0xFF17231D.toInt())
            setPadding(dp(12), dp(13), dp(12), dp(13))
            setBackgroundColor(0xFFFFFFFF.toInt())
        }
        root.addView(statusView, matchWrap())

        receiptPreview = TextView(this).apply {
            textSize = 13f
            setTextColor(0xFF17231D.toInt())
            setPadding(dp(12), dp(10), dp(12), dp(10))
            setBackgroundColor(0xFFE7F3EC.toInt())
            visibility = View.GONE
        }
        root.addView(receiptPreview, matchWrap().apply { topMargin = dp(8) })
        receiptPrintButton = makeButton("Print Receipt") { printInvoiceReceipt() }.apply { visibility = View.GONE }
        root.addView(receiptPrintButton, matchWrap().apply { topMargin = dp(6) })

        val selectorLabel = TextView(this).apply {
            text = "Paired printer"
            textSize = 13f
            typeface = Typeface.DEFAULT_BOLD
            setTextColor(0xFF637168.toInt())
            setPadding(0, dp(17), 0, dp(5))
        }
        root.addView(selectorLabel, matchWrap())

        spinnerAdapter = ArrayAdapter(this, android.R.layout.simple_spinner_item, mutableListOf("Tap Refresh Paired Devices"))
        spinnerAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        deviceSpinner = Spinner(this).apply { adapter = spinnerAdapter }
        root.addView(deviceSpinner, matchWrap())

        refreshButton = makeButton("Refresh Paired Devices") { refreshPairedDevices() }
        root.addView(refreshButton, matchWrap().apply { topMargin = dp(10) })

        val connectionRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
        }
        connectButton = makeButton("Connect") { connectSelectedPrinter() }
        disconnectButton = makeButton("Disconnect") { disconnectPrinter("Disconnected by user.") }
        connectionRow.addView(connectButton, weightedButtonParams())
        connectionRow.addView(disconnectButton, weightedButtonParams().apply { marginStart = dp(9) })
        root.addView(connectionRow, matchWrap().apply { topMargin = dp(9) })

        val diagnosticsTitle = TextView(this).apply {
            text = "Printer diagnostics"
            textSize = 13f
            typeface = Typeface.DEFAULT_BOLD
            setTextColor(0xFF637168.toInt())
            setPadding(0, dp(13), 0, dp(0))
        }
        root.addView(diagnosticsTitle, matchWrap())
        printButton = makeButton("Print Test Receipt") { printTestReceipt() }
        root.addView(printButton, matchWrap().apply { topMargin = dp(9) })

        val logTitle = TextView(this).apply {
            text = "Diagnostic log"
            textSize = 15f
            typeface = Typeface.DEFAULT_BOLD
            setTextColor(0xFF17231D.toInt())
            setPadding(0, dp(19), 0, dp(6))
        }
        root.addView(logTitle, matchWrap())

        logScroll = ScrollView(this).apply {
            isFillViewport = true
            setBackgroundColor(0xFFFFFFFF.toInt())
            setPadding(dp(10), dp(7), dp(10), dp(7))
        }
        logView = TextView(this).apply {
            textSize = 12f
            typeface = Typeface.create("monospace", Typeface.NORMAL)
            setTextColor(0xFF26352C.toInt())
            setTextIsSelectable(true)
        }
        logScroll.addView(logView, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        root.addView(logScroll, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f).apply { topMargin = dp(2) })

        val warning = TextView(this).apply {
            text = "Test-only ASCII receipt. Physical paper output is required to confirm compatibility."
            textSize = 12f
            setTextColor(0xFF637168.toInt())
            setPadding(0, dp(10), 0, 0)
        }
        root.addView(warning, matchWrap())
        setContentView(root)
    }

    private fun makeButton(label: String, action: () -> Unit): Button = Button(this).apply {
        text = label
        isAllCaps = false
        minHeight = dp(48)
        setOnClickListener { action() }
    }

    private fun matchWrap() = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)

    private fun weightedButtonParams() = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private fun hasBluetoothConnectPermission(): Boolean = Build.VERSION.SDK_INT < Build.VERSION_CODES.S ||
        checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED

    private fun requireBluetoothConnectPermission(): Boolean {
        if (hasBluetoothConnectPermission()) return true
        appendLog("Requesting Nearby devices permission (Bluetooth connect) required to list paired devices and connect.")
        requestPermissions(arrayOf(Manifest.permission.BLUETOOTH_CONNECT), REQUEST_BLUETOOTH_CONNECT)
        return false
    }

    @SuppressLint("MissingPermission")
    private fun refreshPairedDevices() {
        if (!requireBluetoothConnectPermission()) return
        val adapter = bluetoothAdapter
        if (adapter == null) {
            appendLog("Bluetooth unsupported: this Android device has no Bluetooth adapter.")
            toast("Bluetooth is not supported on this device.")
            return
        }
        try {
            if (!adapter.isEnabled) {
                appendLog("Bluetooth is disabled. Enable Bluetooth in Android Settings, then refresh again.")
                toast("Enable Bluetooth in Android Settings first.")
                return
            }
            val devices = adapter.bondedDevices.orEmpty().sortedWith(
                compareBy<BluetoothDevice> { !it.name.orEmpty().equals(PRINTER_NAME, ignoreCase = true) }
                    .thenBy { it.name ?: "" }
            )
            pairedDevices.clear()
            pairedDevices.addAll(devices)
            spinnerAdapter.clear()
            if (devices.isEmpty()) {
                spinnerAdapter.add("No paired Bluetooth devices")
                appendLog("No paired devices found. Pair MT580P from Android Bluetooth Settings first.")
            } else {
                devices.forEach { device ->
                    val name = device.name ?: "Unnamed device"
                    val shortId = device.address?.takeLast(5) ?: "-----"
                    spinnerAdapter.add("$name ··$shortId · ${bondStateLabel(device.bondState)}")
                }
                spinnerAdapter.notifyDataSetChanged()
                val remembered = printerPreferences.getString("device_address", null)
                val printerIndex = devices.indexOfFirst { it.address == remembered }.takeIf { it >= 0 }
                    ?: devices.indexOfFirst { it.name.orEmpty().equals(PRINTER_NAME, ignoreCase = true) }
                if (remembered != null && devices.none { it.address == remembered }) {
                    appendLog("Remembered printer is unavailable. Select another paired printer or pair MT580P in Android Settings.")
                }
                if (printerIndex >= 0) {
                    deviceSpinner.setSelection(printerIndex)
                    appendLog("MT580P is paired and selected. No connection has been started.")
                } else {
                    appendLog("MT580P is not paired. Pair it from Android Bluetooth Settings first.")
                }
                appendLog("Loaded ${devices.size} paired device(s). Device identifiers are abbreviated in the selector.")
            }
        } catch (error: SecurityException) {
            appendLog("Permission denied while reading paired devices: ${error.message ?: "Bluetooth permission required."}")
            toast("Allow Nearby devices permission and refresh again.")
        } catch (error: Exception) {
            appendLog("Could not read paired devices: ${error.javaClass.simpleName}: ${error.message ?: "Unknown error"}")
        }
    }

    private fun bondStateLabel(state: Int): String = when (state) {
        BluetoothDevice.BOND_BONDED -> "Paired"
        BluetoothDevice.BOND_BONDING -> "Pairing"
        else -> "Not paired"
    }

    @SuppressLint("MissingPermission")
    private fun connectSelectedPrinter() {
        if (!requireBluetoothConnectPermission()) return
        if (connecting || connectedSocket?.isConnected == true) {
            appendLog("Already connecting or connected.")
            return
        }
        val adapter = bluetoothAdapter
        if (adapter == null) {
            appendLog("Bluetooth unsupported: this Android device has no Bluetooth adapter.")
            return
        }
        try {
            if (!adapter.isEnabled) {
                appendLog("Bluetooth is disabled. Enable Bluetooth in Android Settings, then try Connect again.")
                return
            }
            val device = pairedDevices.getOrNull(deviceSpinner.selectedItemPosition)
            if (device == null) {
                appendLog("No paired printer is selected. Tap Refresh Paired Devices first.")
                return
            }
            printerPreferences.edit().putString("device_address", device.address).apply()

            connecting = true
            updateStatus("Connecting")
            updateControls()
            appendLog("Connection started for ${device.name ?: "selected printer"} using Bluetooth Classic RFCOMM/SPP.")
            appendLog("RFCOMM service UUID: ${ClassicBluetoothTransport.SPP_UUID}")

            val socket = transport.createSocket(device)
            pendingSocket = socket
            timedOutSocket = null
            connectTimeoutTask = Runnable {
                if (pendingSocket === socket) {
                    pendingSocket = null
                    timedOutSocket = socket
                    transport.closeQuietly(socket)
                    connecting = false
                    updateStatus("Disconnected")
                    updateControls()
                    appendLog("Connection timed out after ${CONNECT_TIMEOUT_MS / 1000} seconds. Check printer power, range, pairing, and Classic SPP support.")
                }
            }
            mainHandler.postDelayed(connectTimeoutTask!!, CONNECT_TIMEOUT_MS)

            ioExecutor.execute {
                try {
                    transport.connect(socket)
                    if (pendingSocket !== socket) {
                        transport.closeQuietly(socket)
                        return@execute
                    }
                    pendingSocket = null
                    connectedSocket = socket
                    mainHandler.removeCallbacks(connectTimeoutTask!!)
                    connecting = false
                    runOnUiThread {
                        updateStatus("Connected")
                        updateControls()
                        appendLog("Connection successful. Socket transport: RFCOMM/SPP.")
                    }
                } catch (error: Exception) {
                    transport.closeQuietly(socket)
                    val wasTimedOut = timedOutSocket === socket
                    if (pendingSocket === socket) pendingSocket = null
                    if (wasTimedOut) timedOutSocket = null
                    mainHandler.removeCallbacks(connectTimeoutTask!!)
                    connecting = false
                    runOnUiThread {
                        if (wasTimedOut) {
                            appendLog("RFCOMM connect ended after timeout: ${error.javaClass.simpleName}: ${error.message ?: "No further detail"}")
                        } else if (error is SecurityException) {
                            updateStatus("Disconnected")
                            appendLog("Permission denied during Bluetooth connection. Allow Nearby devices permission and retry.")
                        } else {
                            updateStatus("Disconnected")
                            appendLog("RFCOMM/SPP connection failed: ${error.javaClass.simpleName}: ${error.message ?: "No further detail"}")
                            appendLog("Confirm MT580P is powered, paired, nearby, and accepts Bluetooth Classic SPP. BLE GATT visibility alone does not prove SPP support.")
                        }
                        updateControls()
                    }
                }
            }
        } catch (error: SecurityException) {
            connecting = false
            updateStatus("Disconnected")
            updateControls()
            appendLog("Permission denied creating RFCOMM connection: ${error.message ?: "Bluetooth connect permission required."}")
        } catch (error: Exception) {
            connecting = false
            updateStatus("Disconnected")
            updateControls()
            appendLog("Could not create RFCOMM socket: ${error.javaClass.simpleName}: ${error.message ?: "No further detail"}")
        }
    }

    private fun printTestReceipt() {
        sendReceipt(EscPosTestReceipt.create(), "diagnostic test")
    }

    private fun printInvoiceReceipt() {
        val request = printRequest
        if (request == null) {
            appendLog("No valid invoice print request is loaded. Return to POS and tap Print Receipt again.")
            return
        }
        sendReceipt(EscPosInvoiceReceipt.create(request), "invoice ${request.invoice.number}")
    }

    private fun sendReceipt(receipt: ByteArray, description: String) {
        val socket = connectedSocket
        if (socket == null || !socket.isConnected) {
            connectedSocket = null
            updateStatus("Disconnected")
            updateControls()
            appendLog("Printer disconnected. Connect MT580P again before printing.")
            return
        }
        if (printing) return

        printing = true
        updateControls()
        appendLog("ESC/POS $description generated: ${receipt.size} bytes.")
        ioExecutor.execute {
            try {
                if (connectedSocket !== socket || !socket.isConnected) throw IOException("Printer socket is no longer connected.")
                transport.writeAndFlush(socket, receipt)
                runOnUiThread {
                    appendLog("Bytes written: ${receipt.size}.")
                    appendLog("Flush completed. Check for physical paper output; socket success alone is not proof of printing.")
                    printing = false
                    updateControls()
                }
            } catch (error: Exception) {
                transport.closeQuietly(socket)
                if (connectedSocket === socket) connectedSocket = null
                runOnUiThread {
                    printing = false
                    updateStatus("Disconnected")
                    updateControls()
                    appendLog("Write failure / printer disconnected: ${error.javaClass.simpleName}: ${error.message ?: "No further detail"}")
                    appendLog("Reconnect the paired MT580P and retry the test receipt.")
                }
            }
        }
    }

    override fun onNewIntent(intent: android.content.Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handlePrintIntent(intent)
    }

    private fun handlePrintIntent(intent: android.content.Intent?) {
        val uri: Uri = intent?.data ?: return
        if (uri.scheme != "khatadhari-print" || uri.host != "receipt") return
        val requestGeneration = ++printRequestGeneration
        val api = uri.getQueryParameter("api")
        val reference = uri.getQueryParameter("reference")
        printRequest = null
        receiptPreview.visibility = View.VISIBLE
        receiptPreview.text = "Loading finalized invoice from WhatsBiz..."
        receiptPrintButton.visibility = View.GONE
        appendLog("WhatsBiz print request received. Retrieving invoice using short-lived reference.")
        ioExecutor.execute {
            try {
                val result = PrintBridgeApi.fetch(api, reference)
                runOnUiThread {
                    if (requestGeneration != printRequestGeneration || isFinishing || isDestroyed) return@runOnUiThread
                    printRequest = result
                    receiptPreview.text = buildString {
                        append("${result.business.name}\nInvoice ${result.invoice.number} · ${result.invoice.date}\n")
                        append("${result.invoice.items.size} item(s) · ${result.invoice.status}\n")
                        append("Total Rs. ${result.invoice.total.toPlainString()} · ${result.invoice.customer ?: "Walk-in"}")
                    }
                    receiptPrintButton.visibility = View.VISIBLE
                    updateControls()
                    appendLog("Finalized invoice ${result.invoice.number} loaded for preview. No ERP values were recalculated.")
                }
            } catch (error: Exception) {
                runOnUiThread {
                    if (requestGeneration != printRequestGeneration || isFinishing || isDestroyed) return@runOnUiThread
                    receiptPreview.text = error.message ?: "Invoice request could not be loaded. Return to POS and retry."
                    appendLog("Print request failed: ${error.javaClass.simpleName}: ${error.message ?: "No further detail"}")
                }
            }
        }
    }

    private fun disconnectPrinter(reason: String) {
        mainHandler.removeCallbacks(connectTimeoutTask ?: Runnable {})
        val socket = pendingSocket ?: connectedSocket
        pendingSocket = null
        connectedSocket = null
        timedOutSocket = null
        connecting = false
        printing = false
        transport.closeQuietly(socket)
        updateStatus("Disconnected")
        updateControls()
        appendLog(reason)
        appendLog("Socket closed.")
    }

    private fun updateStatus(value: String) {
        if (Looper.myLooper() != Looper.getMainLooper()) {
            runOnUiThread { updateStatus(value) }
            return
        }
        statusView.text = "Connection: $value"
        statusView.setTextColor(if (value == "Connected") 0xFF176B48.toInt() else 0xFF17231D.toInt())
    }

    private fun updateControls() {
        if (!::connectButton.isInitialized) return
        val connected = connectedSocket?.isConnected == true
        refreshButton.isEnabled = !connecting && !connected
        deviceSpinner.isEnabled = !connecting && !connected
        connectButton.isEnabled = !connecting && !connected
        printButton.isEnabled = connected && !printing
        receiptPrintButton.isEnabled = connected && !printing && printRequest != null
        disconnectButton.isEnabled = connecting || connected
    }

    private fun appendLog(message: String) {
        if (Looper.myLooper() != Looper.getMainLooper()) {
            runOnUiThread { appendLog(message) }
            return
        }
        val time = SimpleDateFormat("HH:mm:ss", Locale.getDefault()).format(Date())
        logLines += "$time  $message"
        while (logLines.size > LOG_LINE_LIMIT) logLines.removeAt(0)
        logView.text = logLines.joinToString("\n")
        logScroll.post { logScroll.fullScroll(ScrollView.FOCUS_DOWN) }
    }

    private fun toast(message: String) = Toast.makeText(this, message, Toast.LENGTH_LONG).show()

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode != REQUEST_BLUETOOTH_CONNECT) return
        if (grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) {
            appendLog("Nearby devices permission granted. Tap Connect if you were trying to connect.")
            refreshPairedDevices()
        } else {
            appendLog("Bluetooth permission denied. Grant Nearby devices permission in Android Settings to continue.")
            toast("Nearby devices permission is required for Bluetooth.")
        }
    }

    override fun onDestroy() {
        mainHandler.removeCallbacks(connectTimeoutTask ?: Runnable {})
        transport.closeQuietly(pendingSocket)
        transport.closeQuietly(connectedSocket)
        pendingSocket = null
        connectedSocket = null
        ioExecutor.shutdownNow()
        super.onDestroy()
    }
}

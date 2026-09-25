package com.khatadhari.printbridge.bluetooth

import android.annotation.SuppressLint
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothSocket
import java.io.IOException
import java.util.UUID

/** Classic RFCOMM/SPP socket transport. Call connect/write/close off the UI thread. */
class ClassicBluetoothTransport {
    companion object {
        val SPP_UUID: UUID = UUID.fromString("00001101-0000-1000-8000-00805F9B34FB")
    }

    @SuppressLint("MissingPermission")
    @Throws(IOException::class, SecurityException::class)
    fun createSocket(device: BluetoothDevice): BluetoothSocket = device.createRfcommSocketToServiceRecord(SPP_UUID)

    @Throws(IOException::class)
    fun connect(socket: BluetoothSocket) = socket.connect()

    @Throws(IOException::class)
    fun writeAndFlush(socket: BluetoothSocket, bytes: ByteArray) {
        socket.outputStream.write(bytes)
        socket.outputStream.flush()
    }

    fun closeQuietly(socket: BluetoothSocket?) {
        try {
            socket?.close()
        } catch (_: IOException) {
            // Closing during cancellation/disconnect is best-effort.
        }
    }
}

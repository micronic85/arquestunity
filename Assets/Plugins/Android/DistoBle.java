package com.vorotakraft.disto;

import android.annotation.SuppressLint;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothProfile;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanResult;
import android.bluetooth.le.ScanSettings;
import android.content.Context;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import com.unity3d.player.UnityPlayer;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayDeque;
import java.util.UUID;

/**
 * Leica DISTO (D110 и др.) по Bluetooth LE.
 * Протокол проверен в vorotych-project (apps/crm/src/zamer/tape.ts):
 * сервис 3ab10100-…, каждый замер приходит уведомлением — float32 little-endian в метрах.
 * Подписываемся на все notify/indicate-характеристики сервиса и берём 4-байтные значения
 * в правдоподобном диапазоне.
 *
 * В Unity: DistoBle.start(context, "ИмяGameObject") → сообщения
 *   OnDistoState("scanning|connecting|on|lost|off|error:…|name:<имя>")
 *   OnDistoValue("<мм>")
 */
@SuppressLint("MissingPermission") // разрешения запрашивает Unity перед start()
public class DistoBle {
    private static final String TAG = "DistoBle";
    private static final UUID SERVICE = UUID.fromString("3ab10100-f831-4395-b29d-570977d5bf94");
    private static final UUID CCCD = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");
    private static final long SCAN_TIMEOUT_MS = 20000;

    private static DistoBle instance;

    private final Context context;
    private final String unityObject;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final ArrayDeque<BluetoothGattDescriptor> pendingDescriptors = new ArrayDeque<>();

    private BluetoothAdapter adapter;
    private BluetoothLeScanner scanner;
    private BluetoothGatt gatt;
    private boolean scanning;
    private boolean stopped;

    private DistoBle(Context context, String unityObject) {
        this.context = context.getApplicationContext();
        this.unityObject = unityObject;
    }

    // ---------- API для Unity ----------

    public static synchronized void start(Context context, String unityObject) {
        if (instance != null) instance.close();
        instance = new DistoBle(context, unityObject);
        instance.main.post(instance::begin);
    }

    public static synchronized void stop() {
        if (instance != null) {
            instance.close();
            instance = null;
        }
    }

    // ---------- scan ----------

    private void begin() {
        BluetoothManager bm = (BluetoothManager) context.getSystemService(Context.BLUETOOTH_SERVICE);
        adapter = bm != null ? bm.getAdapter() : null;
        if (adapter == null) { send("OnDistoState", "error:нет Bluetooth"); return; }
        if (!adapter.isEnabled()) { send("OnDistoState", "error:Bluetooth выключен"); return; }
        scanner = adapter.getBluetoothLeScanner();
        if (scanner == null) { send("OnDistoState", "error:BLE недоступен"); return; }

        scanning = true;
        send("OnDistoState", "scanning");
        ScanSettings settings = new ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY).build();
        try {
            scanner.startScan(null, settings, scanCallback);
        } catch (SecurityException e) {
            scanning = false;
            send("OnDistoState", "error:нет разрешения Bluetooth");
            return;
        }
        main.postDelayed(() -> {
            if (scanning) {
                stopScan();
                send("OnDistoState", "error:рулетка не найдена — включите Bluetooth на DISTO");
            }
        }, SCAN_TIMEOUT_MS);
    }

    private final ScanCallback scanCallback = new ScanCallback() {
        @Override
        public void onScanResult(int callbackType, ScanResult result) {
            BluetoothDevice d = result.getDevice();
            String name = d.getName();
            if (name == null && result.getScanRecord() != null) name = result.getScanRecord().getDeviceName();
            if (name == null || !name.toUpperCase().startsWith("DISTO")) return;
            if (!scanning) return;
            stopScan();
            Log.i(TAG, "found " + name + " " + d.getAddress());
            send("OnDistoState", "name:" + name);
            connect(d);
        }

        @Override
        public void onScanFailed(int errorCode) {
            scanning = false;
            send("OnDistoState", "error:поиск не удался (" + errorCode + ")");
        }
    };

    private void stopScan() {
        scanning = false;
        try { if (scanner != null) scanner.stopScan(scanCallback); } catch (Exception ignored) { }
    }

    // ---------- connect ----------

    private void connect(BluetoothDevice d) {
        send("OnDistoState", "connecting");
        // autoConnect=true: после сна рулетки Android сам переподключится, когда она проснётся
        gatt = d.connectGatt(context, true, gattCallback, BluetoothDevice.TRANSPORT_LE);
    }

    private final BluetoothGattCallback gattCallback = new BluetoothGattCallback() {
        @Override
        public void onConnectionStateChange(BluetoothGatt g, int status, int newState) {
            if (newState == BluetoothProfile.STATE_CONNECTED) {
                main.post(g::discoverServices);
            } else if (newState == BluetoothProfile.STATE_DISCONNECTED && !stopped) {
                send("OnDistoState", "lost");
            }
        }

        @Override
        public void onServicesDiscovered(BluetoothGatt g, int status) {
            BluetoothGattService s = g.getService(SERVICE);
            if (s == null) { send("OnDistoState", "error:у устройства нет сервиса DISTO"); return; }
            pendingDescriptors.clear();
            for (BluetoothGattCharacteristic c : s.getCharacteristics()) {
                int p = c.getProperties();
                boolean notify = (p & BluetoothGattCharacteristic.PROPERTY_NOTIFY) != 0;
                boolean indicate = (p & BluetoothGattCharacteristic.PROPERTY_INDICATE) != 0;
                if (!notify && !indicate) continue;
                g.setCharacteristicNotification(c, true);
                BluetoothGattDescriptor desc = c.getDescriptor(CCCD);
                if (desc != null) pendingDescriptors.add(desc);
            }
            writeNextDescriptor(g);
        }

        @Override
        public void onDescriptorWrite(BluetoothGatt g, BluetoothGattDescriptor d, int status) {
            writeNextDescriptor(g);
        }

        // Android 13+
        @Override
        public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c, byte[] value) {
            handleValue(value);
        }

        // до Android 13
        @Override
        @SuppressWarnings("deprecation")
        public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c) {
            handleValue(c.getValue());
        }
    };

    // Запись дескрипторов в BLE — строго по одному, следующий после onDescriptorWrite
    @SuppressWarnings("deprecation")
    private void writeNextDescriptor(BluetoothGatt g) {
        BluetoothGattDescriptor d = pendingDescriptors.poll();
        if (d == null) { send("OnDistoState", "on"); return; }
        int p = d.getCharacteristic().getProperties();
        byte[] v = (p & BluetoothGattCharacteristic.PROPERTY_NOTIFY) != 0
                ? BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE
                : BluetoothGattDescriptor.ENABLE_INDICATION_VALUE;
        if (Build.VERSION.SDK_INT >= 33) {
            g.writeDescriptor(d, v);
        } else {
            d.setValue(v);
            g.writeDescriptor(d);
        }
    }

    private void handleValue(byte[] v) {
        if (v == null || v.length != 4) return;
        float metres = ByteBuffer.wrap(v).order(ByteOrder.LITTLE_ENDIAN).getFloat();
        if (Float.isNaN(metres) || Float.isInfinite(metres) || metres <= 0f || metres > 300f) return;
        send("OnDistoValue", Integer.toString(Math.round(metres * 1000f)));
    }

    // ---------- close ----------

    private void close() {
        stopped = true;
        stopScan();
        main.removeCallbacksAndMessages(null);
        if (gatt != null) {
            try { gatt.disconnect(); gatt.close(); } catch (Exception ignored) { }
            gatt = null;
        }
        send("OnDistoState", "off");
    }

    private void send(String method, String msg) {
        Log.i(TAG, method + " " + msg);
        try { UnityPlayer.UnitySendMessage(unityObject, method, msg); } catch (Throwable ignored) { }
    }
}

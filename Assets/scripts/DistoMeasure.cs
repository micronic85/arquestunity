using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Замер проёма лазерной рулеткой Leica DISTO (D110) по Bluetooth LE.
/// Android-часть — Assets/Plugins/Android/DistoBle.java; она шлёт сюда OnDistoState / OnDistoValue
/// через UnitySendMessage, поэтому имя GameObject должно быть уникальным.
/// Замер записывается в поле Next (ширина → высота → ширина …); поле можно выбрать вручную.
/// </summary>
public class DistoMeasure : MonoBehaviour
{
    public enum Field { Width, Height }

    [Tooltip("Подключаться к рулетке сразу при запуске")]
    public bool connectOnStart = true;
    public bool debugLogs = false;

    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public Field Next { get; private set; } = Field.Width;
    public string Status { get; private set; } = "не подключено";
    public string DeviceName { get; private set; }

    /// <summary>Что-то изменилось (замер, поле, статус) — перерисовать UI.</summary>
    public event Action Changed;

    const string PluginClass = "com.vorotakraft.disto.DistoBle";
    static readonly string[] Permissions = { "android.permission.BLUETOOTH_SCAN", "android.permission.BLUETOOTH_CONNECT" };

    void Start()
    {
        if (connectOnStart) Connect();
    }

    // ---------- управление ----------

    public void Connect()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (HasPermissions()) { StartPlugin(); return; }
        SetStatus("нужно разрешение Bluetooth");
        var cb = new PermissionCallbacks();
        cb.PermissionGranted += _ => { if (HasPermissions()) StartPlugin(); };
        cb.PermissionDenied += _ => SetStatus("нет разрешения Bluetooth");
        cb.PermissionDeniedAndDontAskAgain += _ => SetStatus("Bluetooth запрещён — разрешите в настройках приложения");
        Permission.RequestUserPermissions(Permissions, cb);
#else
        SetStatus("рулетка работает только на шлеме (в редакторе — ПКМ по компоненту → Тест)");
#endif
    }

    public void ResetValues()
    {
        Width = null;
        Height = null;
        Next = Field.Width;
        Changed?.Invoke();
    }

    public void SetNext(Field f)
    {
        Next = f;
        Changed?.Invoke();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    static bool HasPermissions()
    {
        foreach (var p in Permissions)
            if (!Permission.HasUserAuthorizedPermission(p)) return false;
        return true;
    }

    void StartPlugin()
    {
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var plugin = new AndroidJavaClass(PluginClass);
            plugin.CallStatic("start", activity, gameObject.name);
        }
        catch (Exception e)
        {
            SetStatus("ошибка плагина: " + e.Message);
            Debug.LogException(e, this);
        }
    }

    void OnDestroy()
    {
        try
        {
            using var plugin = new AndroidJavaClass(PluginClass);
            plugin.CallStatic("stop");
        }
        catch { /* приложение закрывается */ }
    }
#endif

    // ---------- сообщения из Java ----------

    // ReSharper disable once UnusedMember.Local — вызывается через UnitySendMessage
    void OnDistoState(string s)
    {
        if (debugLogs) Debug.Log("DistoMeasure state: " + s);
        if (s.StartsWith("name:")) { DeviceName = s.Substring(5); return; }
        if (s.StartsWith("error:")) { SetStatus(s.Substring(6)); return; }
        switch (s)
        {
            case "scanning": SetStatus("поиск рулетки…"); break;
            case "connecting": SetStatus("подключение…"); break;
            case "on": SetStatus("подключено" + (DeviceName != null ? ": " + DeviceName : "")); break;
            case "lost": SetStatus("связь потеряна — нажмите кнопку на рулетке"); break;
            case "off": SetStatus("выключено"); break;
            default: SetStatus(s); break;
        }
    }

    void OnDistoValue(string mmText)
    {
        if (!int.TryParse(mmText, out int mm)) return;
        if (debugLogs) Debug.Log($"DistoMeasure: {mm} мм → {Next}");
        if (Next == Field.Width) { Width = mm; Next = Field.Height; }
        else { Height = mm; Next = Field.Width; }
        Changed?.Invoke();
    }

    void SetStatus(string s)
    {
        Status = s;
        Changed?.Invoke();
    }

    [ContextMenu("Тест: замер 2985 мм")]
    void TestValue() => OnDistoValue("2985");

    public static string Format(int? mm) => mm.HasValue ? mm.Value.ToString("# ##0").Trim() + " мм" : "—";
}

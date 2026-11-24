using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>Explicit, confirmable dimension editing with a palm-menu numeric keypad.</summary>
public partial class GateColorPicker
{
    bool _manualEditing;
    bool _manualReplace;
    DistoMeasure.Field _manualField;
    string _manualDraft = "";
    TextMeshPro _manualDraftText;

    void BeginManualInput(DistoMeasure.Field field)
    {
        if (measure == null) return;
        _manualEditing = true;
        _manualField = field;
        int? current = field == DistoMeasure.Field.Width ? measure.Width : measure.Height;
        _manualDraft = current?.ToString(CultureInfo.InvariantCulture) ?? "";
        _manualReplace = true;
        measure.SetNext(field);
        RebuildMenu();
        SetStatus("Введите размер в миллиметрах");
    }

    void ManualDigit(int digit)
    {
        if (!_manualEditing || digit < 0 || digit > 9) return;
        if (_manualReplace || _manualDraft == "0") _manualDraft = "";
        _manualReplace = false;
        if (_manualDraft.Length < 5) _manualDraft += digit.ToString(CultureInfo.InvariantCulture);
        UpdateManualDisplay();
    }

    void ManualBackspace()
    {
        if (!_manualEditing) return;
        _manualReplace = false;
        if (_manualDraft.Length > 0) _manualDraft = _manualDraft.Substring(0,_manualDraft.Length-1);
        UpdateManualDisplay();
    }

    void ManualClear()
    {
        if (!_manualEditing) return;
        _manualDraft = "";
        _manualReplace = false;
        UpdateManualDisplay();
    }

    void ConfirmManualInput()
    {
        if (!_manualEditing || measure == null) return;
        if (!int.TryParse(_manualDraft,NumberStyles.None,CultureInfo.InvariantCulture,out int value) ||
            !measure.TrySetValue(_manualField,value))
        {
            SetStatus("Введите положительный размер в мм");
            return;
        }
        string label = _manualField == DistoMeasure.Field.Width ? "Ширина" : "Высота";
        _manualEditing = false;
        RebuildMenu();
        SetStatus(label + ": " + DistoMeasure.Format(value) + " (вручную)");
    }

    void CancelManualInput()
    {
        _manualEditing = false;
        RebuildMenu();
        SetStatus("Ручной ввод отменён");
    }

    void UpdateManualDisplay()
    {
        if (_manualDraftText == null) return;
        string label = _manualField == DistoMeasure.Field.Width ? "Ширина" : "Высота";
        _manualDraftText.text = label + ": " + (_manualDraft.Length > 0 ? _manualDraft : "—") + " мм";
        _manualDraftText.color = _manualReplace ? ColAccent : ColText;
    }

    float BuildManualKeypad(float y,float width,float height,float gap)
    {
        var display = Text("ManualDimensionDraft",_menu,"",width,height*.7f);
        display.localPosition = new Vector3(0,y-height*.5f,-.001f);
        _manualDraftText = display.GetComponent<TextMeshPro>();
        UpdateManualDisplay();
        y -= height + gap;
        float cell = (width-2*gap)/3;
        int[] digits = {7,8,9,4,5,6,1,2,3};
        for (int i=0;i<digits.Length;i++)
        {
            int digit = digits[i];
            float x = -width*.5f+cell*.5f+(i%3)*(cell+gap);
            float keyY = y-(i/3)*(height+gap)-height*.5f;
            PanelButton("ManualDigit_"+digit,new Vector3(x,keyY,0),cell,height,out var label,
                () => ManualDigit(digit),.65f);
            label.text = digit.ToString(CultureInfo.InvariantCulture);
        }
        y -= 3*(height+gap);
        PanelButton("ManualClear",new Vector3(-width*.5f+cell*.5f,y-height*.5f,0),cell,height,
            out var clear,ManualClear); clear.text="Очистить";
        PanelButton("ManualDigit_0",new Vector3(0,y-height*.5f,0),cell,height,
            out var zero,()=>ManualDigit(0),.65f); zero.text="0";
        PanelButton("ManualBackspace",new Vector3(width*.5f-cell*.5f,y-height*.5f,0),cell,height,
            out var back,ManualBackspace); back.text="Стереть";
        y -= height+gap;
        float half = (width-gap)*.5f;
        PanelButton("ManualCancel",new Vector3(-width*.5f+half*.5f,y-height*.5f,0),half,height,
            out var cancel,CancelManualInput); cancel.text="Отмена";
        var confirm = PanelButton("ManualConfirm",new Vector3(width*.5f-half*.5f,y-height*.5f,0),half,height,
            out var done,ConfirmManualInput); done.text="Готово";
        confirm.baseColor=ColAccent; confirm.SetColor(ColAccent);
        return y-height-gap;
    }
}

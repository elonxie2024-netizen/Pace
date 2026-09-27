using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class PaceTheme {
    public static bool Dark { get; private set; }
    public static Color Background { get { return Dark?Color.FromArgb(18,25,23):Color.FromArgb(247,244,235); } }
    public static Color Surface { get { return Dark?Color.FromArgb(27,37,33):Color.FromArgb(252,251,247); } }
    public static Color Card { get { return Dark?Color.FromArgb(35,49,43):Color.FromArgb(224,235,225); } }
    public static Color Input { get { return Dark?Color.FromArgb(42,55,49):Color.FromArgb(250,249,244); } }
    public static Color Text { get { return Dark?Color.FromArgb(226,235,229):Color.FromArgb(38,58,53); } }
    public static Color Muted { get { return Dark?Color.FromArgb(164,184,173):Color.FromArgb(91,108,100); } }
    public static Color Accent { get { return Dark?Color.FromArgb(76,157,128):Color.FromArgb(61,128,105); } }
    public static Color AccentHover { get { return Dark?Color.FromArgb(92,176,146):Color.FromArgb(76,145,121); } }
    public static Color AccentDown { get { return Dark?Color.FromArgb(59,130,105):Color.FromArgb(46,105,86); } }
    public static Color Border { get { return Dark?Color.FromArgb(62,80,70):Color.FromArgb(205,218,207); } }
    public static Color Alternate { get { return Dark?Color.FromArgb(31,43,38):Color.FromArgb(246,247,241); } }
    public static Color Reminder { get { return Dark?Color.FromArgb(31,45,39):Color.FromArgb(239,246,239); } }
    public static Color FocusReminder { get { return Dark?Color.FromArgb(53,44,31):Color.FromArgb(250,244,226); } }
    public static Color Error { get { return Dark?Color.FromArgb(240,139,121):Color.FromArgb(178,73,61); } }
    public static Color Warning { get { return Dark?Color.FromArgb(229,177,98):Color.FromArgb(157,96,48); } }
    public static Color ButtonText { get { return Color.White; } }

    public static void SetDark(bool dark) { Dark=dark; }

    public static void Apply(Control root) {
        if(root is Form) { root.BackColor=Background; root.ForeColor=Text; ApplyTitleBar((Form)root); }
        if(root is TabPage || root is ListBox) { root.BackColor=Surface; root.ForeColor=Text; }
        else if(root is ComboBox || root is NumericUpDown || root is TextBox || root is DateTimePicker) { root.BackColor=Input; root.ForeColor=Text; if(root is NumericUpDown)((NumericUpDown)root).TextAlign=HorizontalAlignment.Center; }
        else if(root is Button) {
            Button button=(Button)root; button.BackColor=Accent; button.ForeColor=ButtonText;
            if(button.FlatStyle==FlatStyle.Flat) { button.FlatAppearance.MouseOverBackColor=AccentHover; button.FlatAppearance.MouseDownBackColor=AccentDown; }
        }
        else if(root is CheckBox) { root.BackColor=root.Parent==null?Surface:root.Parent.BackColor; root.ForeColor=Text; }
        else if(root is Label)root.ForeColor=Text;
        foreach(Control child in root.Controls)Apply(child);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    public static void ApplyTitleBar(Form form) {
        if(form==null)return;
        Action apply=delegate {
            try { int enabled=Dark?1:0; int attribute=20; if(DwmSetWindowAttribute(form.Handle,attribute,ref enabled,sizeof(int))!=0) { attribute=19; DwmSetWindowAttribute(form.Handle,attribute,ref enabled,sizeof(int)); } }
            catch { }
        };
        if(form.IsHandleCreated)apply(); else form.HandleCreated+=delegate { apply(); };
    }
}

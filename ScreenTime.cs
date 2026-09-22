using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Diagnostics;
using System.Threading;
using System.Text;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

public class WeeklyPlan {
    public string WeekStart = "";
    public int[] Minutes = new int[7];
}
public class AdvancedBlock {
    public string WeekStart="";
    public int Day;
    public string Start="09:00";
    public string End="10:00";
    public string Activity="";
}
public class PlanChange {
    public string WeekStart="";
    public string ChangedAt="";
    public string Summary="";
}

public class DayRecord {
    public string Date = "";
    public double Used;
    public int Extra;
    public bool Warned, Exhausted, BreakEarned;
    public double SinceReminder;
    public List<string> Reasons = new List<string>();
    public List<ActivityRecord> Activities = new List<ActivityRecord>();
    public string ActiveBlock="";
    public double BlockUsed;
}
public class ActivityRecord {
    public string Name = "";
    public string Category = "Other";
    public double Seconds;
}
public sealed class BreakScreen : Form {
    readonly Label title, timerLabel, note;
    readonly Button cancel, offscreen, continueButton;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=250 };
    DateTime until;
    readonly Action cancelAction, continueAction, offscreenAction;
    readonly Action addTimeAction;
    readonly Button addMore;
    public BreakScreen(Action cancelAction,Action continueAction,Action offscreenAction,Action addTimeAction) {
        this.cancelAction=cancelAction; this.continueAction=continueAction; this.offscreenAction=offscreenAction;
        this.addTimeAction=addTimeAction;
        FormBorderStyle=FormBorderStyle.None; StartPosition=FormStartPosition.Manual; ShowInTaskbar=false; TopMost=true; BackColor=Color.FromArgb(18,38,34); WindowState=FormWindowState.Maximized;
        title=new Label { Text="TAKE A REAL BREAK",Dock=DockStyle.Top,Height=85,TextAlign=ContentAlignment.BottomCenter,ForeColor=Color.FromArgb(222,239,218),Font=new Font("Segoe UI",24) }; Controls.Add(title);
        timerLabel=new Label { Text="00:00",Dock=DockStyle.Top,Height=220,TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.White,Font=new Font("Segoe UI Light",104) }; Controls.Add(timerLabel);
        note=new Label { Text="Step away from the screen. Lock Windows with Win+L if you leave.",Dock=DockStyle.Top,Height=55,TextAlign=ContentAlignment.TopCenter,ForeColor=Color.FromArgb(190,215,203),Font=new Font("Segoe UI",13) }; Controls.Add(note);
        Panel buttons=new Panel { Dock=DockStyle.Bottom,Height=120 }; Controls.Add(buttons);
        cancel=MakeButton("Cancel break",new Point(40,35),cancelAction); buttons.Controls.Add(cancel);
        offscreen=MakeButton("Offscreen activity",new Point(260,35),offscreenAction); buttons.Controls.Add(offscreen);
        continueButton=MakeButton("Continue",new Point(480,35),continueAction); buttons.Controls.Add(continueButton);
        addMore=MakeButton("Add more time",new Point(700,35),addTimeAction); buttons.Controls.Add(addMore);
        timer.Tick+=delegate { UpdateTimer(); }; timer.Start();
        Shown+=delegate { BringToFront(); Activate(); };
    }
    Button MakeButton(string text,Point location,Action action) { Button b=new Button { Text=text,Location=location,Size=new Size(190,50),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(38,125,103),ForeColor=Color.White,Font=new Font("Segoe UI",11) }; b.FlatAppearance.BorderSize=0; b.Click+=delegate { action(); }; return b; }
    public void ShowBreak(DateTime end) { until=end; title.Text="TAKE A REAL BREAK"; note.Text="Step away from the screen. Lock Windows with Win+L if you leave."; continueButton.Visible=false; cancel.Visible=true; offscreen.Visible=true; Show(); WindowState=FormWindowState.Maximized; BringToFront(); UpdateTimer(); }
    public void ShowFinished() { title.Text="BREAK COMPLETE"; note.Text="Your screen time will not resume until you choose Continue."; timerLabel.Text="00:00"; continueButton.Visible=true; cancel.Visible=true; offscreen.Visible=false; Show(); BringToFront(); UpdateTimer(); }
    public void ShowOffscreen() { title.Text="OFFSCREEN ACTIVITY"; note.Text="Continue when you’re ready. Screen time stays stopped until then."; timerLabel.Text=""; continueButton.Visible=true; cancel.Visible=true; offscreen.Visible=false; Show(); BringToFront(); }
    public void ShowDailyShutdown() { ShowDailyShutdown("Your daily screen-time plan is complete. Add more time if you have a reason."); }
    public void ShowDailyShutdown(string message) { title.Text="OFFSCREEN ACTIVITY"; note.Text=message; timerLabel.Text=""; continueButton.Visible=false; cancel.Visible=false; offscreen.Visible=false; addMore.Visible=true; Show(); WindowState=FormWindowState.Maximized; BringToFront(); }
    public void SetOffscreen() { ShowOffscreen(); }
    void UpdateTimer() { double left=(until-DateTime.UtcNow).TotalSeconds; if(continueButton.Visible)timerLabel.Text="00:00"; else timerLabel.Text=left<=0?"00:00":((int)left/60).ToString("00")+":"+((int)left%60).ToString("00"); }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { CreateParams p=base.CreateParams; p.ExStyle|=0x00000080; return p; } }
    protected override void Dispose(bool disposing) { if(disposing)timer.Dispose(); base.Dispose(disposing); }
}
public class Settings {
    // Retained only to read old files; never used as a repeating weekly budget.
    public int[] Plan = {120,120,120,120,120,180,180};
    public List<WeeklyPlan> Weeks = new List<WeeklyPlan>();
    public List<PlanChange> PlanChanges = new List<PlanChange>();
    public bool AdvancedPlan;
    public List<AdvancedBlock> Blocks = new List<AdvancedBlock>();
    public int Interval = 20, BreakMinutes = 5;
    public int AlertVolume = 85;
    public DateTime BreakUntil = DateTime.MinValue;
    public bool BreakWaiting;
    public bool BreakOffscreen;
    public bool DailyShutdown;
    public List<DayRecord> Days = new List<DayRecord>();
    public static DateTime Monday(DateTime date) { return date.Date.AddDays(-((int)date.DayOfWeek+6)%7); }
    public WeeklyPlan GetWeek(DateTime date) { string key=Monday(date).ToString("yyyy-MM-dd"); return Weeks.Find(w=>w.WeekStart==key); }
    public void SetWeek(DateTime date,int[] minutes) {
        if(minutes==null || minutes.Length!=7 || Array.Exists(minutes,m=>m<0 || m>1440))throw new ArgumentException("Use seven daily budgets between 0 and 1440 minutes.");
        WeeklyPlan week=GetWeek(date); if(week==null) { week=new WeeklyPlan { WeekStart=Monday(date).ToString("yyyy-MM-dd") }; Weeks.Add(week); }
        week.Minutes=(int[])minutes.Clone();
    }
}
public class ScreenTime : Form {
    Settings state;
    DayRecord day;
    readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrustScreenTime");
    string StatePath { get { return Path.Combine(folder,"state.xml"); } }
    Label remaining, detail, status;
    ProgressBar progress;
    Button rest, extra;
    ComboBox weekPicker;
    Label weekHint, todayLabel;
    Label advancedHint;
    TabControl tabs;
    DateTime selectedWeek;
    string promptedWeek="";
    bool sessionLocked;
    NumericUpDown[] planHours = new NumericUpDown[7];
    NumericUpDown[] planMinutes = new NumericUpDown[7];
    NumericUpDown breakMinutes;
    ComboBox interval;
    ComboBox planMode;
    TextBox blockEditor;
    ListBox history;
    NotifyIcon tray;
    System.Windows.Forms.Timer timer;
    Stopwatch watch = Stopwatch.StartNew();
    double last;
    int ticks;
    bool exiting, saveFailed;
    Form toast;
    System.Windows.Forms.Timer cleanupTimer;
    DateTime cleanupUntil;
    Label cleanupLabel;
    bool cleanupActive;
    bool cleanupClosesBlock;
    int cleanupLastPing=-1;
    BreakScreen breakScreen;
    CornerBar cornerBar;
    readonly AlertSound alertSound=new AlertSound();
    bool cornerStarted;
    NumericUpDown alertVolume;
    ListBox activityList;
    ActivityChart activityChart;
    string foregroundName="";
    double foregroundSince;
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    Color ink = Color.FromArgb(38,58,53), green = Color.FromArgb(61,128,105);
    Color cream = Color.FromArgb(247,244,235), sage = Color.FromArgb(224,235,225);
    bool HasPlan { get { return state.GetWeek(DateTime.Now)!=null; } }
    AdvancedBlock ActiveBlock { get { if(!state.AdvancedPlan)return null; string key=Settings.Monday(DateTime.Now).ToString("yyyy-MM-dd"); int dow=(int)DateTime.Now.DayOfWeek; TimeSpan now=DateTime.Now.TimeOfDay; return state.Blocks.Find(b=>b.WeekStart==key && b.Day==dow && TimeSpan.Parse(b.Start)<=now && now<TimeSpan.Parse(b.End)); } }
    int Budget { get { WeeklyPlan week=state.GetWeek(DateTime.Now); AdvancedBlock block=ActiveBlock; if(state.AdvancedPlan)return block==null?0:(int)(TimeSpan.Parse(block.End)-TimeSpan.Parse(block.Start)).TotalSeconds+day.Extra*60; return (week==null?0:week.Minutes[((int)DateTime.Now.DayOfWeek+6)%7]*60)+day.Extra*60; } }
    DateTime? NextBlockStart { get { DateTime monday=Settings.Monday(DateTime.Now); DateTime? best=null; foreach(AdvancedBlock b in state.Blocks)if(b.WeekStart==monday.ToString("yyyy-MM-dd")) { DateTime candidate=monday.AddDays(((b.Day+6)%7)).Add(TimeSpan.Parse(b.Start)); if(candidate>DateTime.Now && (best==null || candidate<best))best=candidate; } return best; } }
    bool Breaking { get { return state.BreakUntil > DateTime.UtcNow; } }
    public ScreenTime() : this(null) { }
    public ScreenTime(string dataFolder) {
        if(dataFolder!=null)folder=dataFolder;
        Text="Pace"; ClientSize=new Size(1120,805); MinimumSize=new Size(1140,845);
        BackColor=cream; ForeColor=ink; Font=new Font("Segoe UI",10); StartPosition=FormStartPosition.CenterScreen;
        string art=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","pace-garden.png"); if(File.Exists(art)) { BackgroundImage=Image.FromFile(art); BackgroundImageLayout=ImageLayout.Stretch; }
        LoadState(); Today(); EnsureCurrentWeekPlan();
        Label brand=AddLabel(this,"PACE",135,30,850,24,10); brand.ForeColor=green; brand.BackColor=Color.Transparent; brand.Font=new Font("Segoe UI Semibold",10);
        Label hero=AddLabel(this,"Make room for life off screen.",135,67,850,52,27); hero.ForeColor=ink; hero.BackColor=Color.Transparent; hero.Font=new Font("Segoe UI Semibold",27);
        Label subtitle=AddLabel(this,"A plan you choose. Gentle reminders. Always built on trust.",137,125,840,30,11); subtitle.ForeColor=Color.FromArgb(91,108,100); subtitle.BackColor=Color.Transparent;
        Panel card=new Panel { Location=new Point(135,169),Size=new Size(850,220),BackColor=sage,Padding=new Padding(2) }; Controls.Add(card); Round(card,18);
        todayLabel=AddLabel(card,"TODAY    "+DateTime.Now.ToString("dddd, MMM d"),22,15,760,25,10);
        remaining=AddLabel(card,"",22,46,780,52,30);
        detail=AddLabel(card,"",24,104,780,26,11);
        progress=new ProgressBar { Location=new Point(24,141), Size=new Size(800,8), Maximum=1000 }; card.Controls.Add(progress);
        rest=ButtonAt(card,"Take a break",24,171,155,delegate { StartBreak(); });
        extra=ButtonAt(card,"+ Add time",192,171,150,delegate { AddTime(); });
        ButtonAt(card,"End screen time",360,171,150,delegate { StartDailyShutdown(); });
        Label tracking=AddLabel(card,"Tracking is automatic while Pace runs.",535,177,285,28,10); tracking.ForeColor=Color.FromArgb(91,108,100);
        status=AddLabel(this,"",137,407,850,28,11); status.ForeColor=Color.FromArgb(76,100,91); status.BackColor=Color.Transparent;
        tabs=new TabControl { Location=new Point(135,453),Size=new Size(850,300),Padding=new Point(18,7),Appearance=TabAppearance.Buttons,DrawMode=TabDrawMode.OwnerDrawFixed,SizeMode=TabSizeMode.Fixed,ItemSize=new Size(150,34) }; tabs.DrawItem+=DrawTab; Controls.Add(tabs); Round(tabs,14);
        TabPage weekly=new TabPage("Weekly plan") { BackColor=Color.FromArgb(252,251,247) }; tabs.TabPages.Add(weekly);
        string[] names={"Mon","Tue","Wed","Thu","Fri","Sat","Sun"};
        weekPicker=new ComboBox { Location=new Point(18,10),Size=new Size(550,28),DropDownStyle=ComboBoxStyle.DropDownList }; weekly.Controls.Add(weekPicker);
        AddLabel(weekly,"Plan type",590,10,70,25,10); planMode=new ComboBox { Location=new Point(660,8),Size=new Size(148,28),DropDownStyle=ComboBoxStyle.DropDownList }; planMode.Items.AddRange(new object[]{"Simple limits","Advanced blocks"}); planMode.SelectedIndex=0; weekly.Controls.Add(planMode);
        for(int i=0;i<7;i++) { int x=18+i*112; AddLabel(weekly,names[i],x,49,99,24,11); planHours[i]=new NumericUpDown { Location=new Point(x,77),Size=new Size(45,28),Minimum=0,Maximum=24 }; planMinutes[i]=new NumericUpDown { Location=new Point(x+48,77),Size=new Size(45,28),Minimum=0,Maximum=59,Increment=5 }; weekly.Controls.Add(planHours[i]); weekly.Controls.Add(planMinutes[i]); }
        weekHint=AddLabel(weekly,"",18,112,800,25,10);
        blockEditor=new TextBox { Location=new Point(18,112),Size=new Size(630,55),Multiline=true,ScrollBars=ScrollBars.Vertical,Visible=false }; weekly.Controls.Add(blockEditor);
        advancedHint=AddLabel(weekly,"Advanced format: Day | Start | End | Activity   (example: Mon | 09:00 | 10:30 | School)",18,169,630,20,9); advancedHint.Visible=false;
        planMode.SelectedIndexChanged+=delegate { bool advanced=planMode.SelectedIndex==1; blockEditor.Visible=advanced; advancedHint.Visible=advanced; weekHint.Visible=!advanced; for(int i=0;i<7;i++) { planHours[i].Visible=!advanced; planMinutes[i].Visible=!advanced; } };
        AddLabel(weekly,"Remind every",18,198,110,25,10);
        interval=new ComboBox { Location=new Point(129,194),Size=new Size(70,28),DropDownStyle=ComboBoxStyle.DropDownList }; interval.Items.AddRange(new object[]{15,20,30}); interval.SelectedItem=state.Interval; weekly.Controls.Add(interval);
        AddLabel(weekly,"min        Break",207,198,120,25,10);
        breakMinutes=new NumericUpDown { Location=new Point(332,194),Size=new Size(65,28),Minimum=1,Maximum=60,Value=state.BreakMinutes }; weekly.Controls.Add(breakMinutes);
        AddLabel(weekly,"min",406,198,45,25,10);
        ButtonAt(weekly,"Save this week",660,190,148,36,delegate { int[] values=new int[7]; for(int i=0;i<7;i++)values[i]=(int)planHours[i].Value*60+(int)planMinutes[i].Value; state.SetWeek(selectedWeek,values); state.AdvancedPlan=planMode.SelectedIndex==1; if(state.AdvancedPlan)SaveBlocks(); state.PlanChanges.Add(new PlanChange { WeekStart=Settings.Monday(selectedWeek).ToString("yyyy-MM-dd"), ChangedAt=DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Summary=state.AdvancedPlan?"Advanced blocks saved":"Plan saved: "+FormatPlan(values) }); state.Interval=(int)interval.SelectedItem; state.BreakMinutes=(int)breakMinutes.Value; if(selectedWeek==Settings.Monday(DateTime.Now)) { day.Warned=false; day.Exhausted=false; } Save(); LoadWeekEditor(); RefreshView(); });
        ButtonAt(weekly,"Copy previous week",465,190,180,36,delegate { WeeklyPlan previous=state.GetWeek(selectedWeek.AddDays(-7)); if(previous==null) { MessageBox.Show("There is no saved plan for the previous week yet.","Copy plan"); return; } for(int i=0;i<7;i++) { planHours[i].Value=previous.Minutes[i]/60; planMinutes[i].Value=previous.Minutes[i]%60; } weekHint.Text="Copied the previous week's plan. Save this week when it looks right."; });
        TabPage log=new TabPage("Time & reasons") { BackColor=Color.FromArgb(252,251,247) }; tabs.TabPages.Add(log);
        history=new ListBox { Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,HorizontalScrollbar=true }; log.Controls.Add(history);
        TabPage activity=new TabPage("Where time went") { BackColor=Color.FromArgb(252,251,247) }; tabs.TabPages.Add(activity);
        AddLabel(activity,"Your screen time, grouped by app or visible browser tab.",18,16,790,28,13);
        AddLabel(activity,"Everything stays local. Browser entries use the visible tab title; exact URLs require a browser extension.",18,48,790,42,10);
        activityChart=new ActivityChart { Location=new Point(18,92),Size=new Size(385,160) }; activity.Controls.Add(activityChart);
        activityList=new ListBox { Location=new Point(420,92),Size=new Size(388,160),BorderStyle=BorderStyle.None,HorizontalScrollbar=true,BackColor=Color.FromArgb(252,251,247) }; activity.Controls.Add(activityList);
        TabPage alerts=new TabPage("Reminders & sound") { BackColor=Color.FromArgb(252,251,247) }; tabs.TabPages.Add(alerts);
        AddLabel(alerts,"A clearer reminder, even when you are busy.",18,18,790,35,16);
        AddLabel(alerts,"Three distinct chimes. Adjust their volume here, then try a reminder.",18,62,790,30,11);
        AddLabel(alerts,"Alert volume",18,112,125,28,11);
        alertVolume=new NumericUpDown { Location=new Point(150,108),Size=new Size(75,28),Minimum=0,Maximum=100,Value=state.AlertVolume }; alerts.Controls.Add(alertVolume);
        AddLabel(alerts,"%",233,112,30,28,11);
        alertVolume.ValueChanged+=delegate { state.AlertVolume=(int)alertVolume.Value; Save(); };
        AddLabel(alerts,"Windows volume and mute still apply. The corner bar stays visible when you close this window.",18,168,790,50,10);
        cornerBar=new CornerBar(); cornerBar.OpenDashboard+=delegate { if(Visible) { Hide(); } else { Show(); WindowState=FormWindowState.Normal; Activate(); } cornerBar.SetDashboardOpen(Visible); }; cornerBar.AddTimeClicked+=delegate { AddTime(); }; cornerBar.TakeBreakClicked+=delegate { StartBreak(); }; cornerBar.EndDayClicked+=delegate { StartDailyShutdown(); };
        tray=new NotifyIcon { Icon=SystemIcons.Application,Text="Screen Time",Visible=true };
        ContextMenuStrip menu=new ContextMenuStrip(); menu.Items.Add("Open Screen Time",null,delegate { Show(); WindowState=FormWindowState.Normal; Activate(); }); menu.Items.Add("Exit (stop tracking)",null,delegate { exiting=true; Close(); }); tray.ContextMenuStrip=menu;
        tray.DoubleClick+=delegate { Show(); WindowState=FormWindowState.Normal; Activate(); };
        FormClosing+=delegate(object s,FormClosingEventArgs e) { if(!exiting && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); } else { Save(); tray.Dispose(); } };
        weekPicker.SelectedIndexChanged+=delegate { selectedWeek=Settings.Monday(DateTime.Now).AddDays(7*Math.Max(0,weekPicker.SelectedIndex)); LoadWeekEditor(); };
        ResetWeekPicker();
        SystemEvents.SessionSwitch+=SessionChanged;
        SystemEvents.PowerModeChanged+=PowerChanged;
        StyleInputs(this);
        timer=new System.Windows.Forms.Timer { Interval=1000 }; timer.Tick+=delegate { Tick(); }; timer.Start(); RefreshView();
        Shown+=delegate { cornerStarted=true; Hide(); UpdateCorner(); if(state.AdvancedPlan && ActiveBlock==null)StartDailyShutdown(); else if(state.DailyShutdown) { if(breakScreen==null)breakScreen=new BreakScreen(CancelBreak,ContinueBreak,OffscreenActivity,AddTime); breakScreen.ShowDailyShutdown(); } else if(state.BreakOffscreen) { if(breakScreen==null)breakScreen=new BreakScreen(CancelBreak,ContinueBreak,OffscreenActivity,AddTime); breakScreen.ShowOffscreen(); } else if(state.BreakWaiting) { if(breakScreen==null)breakScreen=new BreakScreen(CancelBreak,ContinueBreak,OffscreenActivity,AddTime); breakScreen.ShowFinished(); } else if(Breaking) { if(breakScreen==null)breakScreen=new BreakScreen(CancelBreak,ContinueBreak,OffscreenActivity,AddTime); breakScreen.ShowBreak(state.BreakUntil); } PromptForWeek(); };
    }
    Label AddLabel(Control parent,string text,int x,int y,int w,int h,float size) { Label l=new Label { Text=text,Location=new Point(x,y),Size=new Size(w,h),Font=new Font("Segoe UI",size),ForeColor=ink }; parent.Controls.Add(l); return l; }
    static void Round(Control control,int radius) { Action apply=delegate { if(control.Width<2 || control.Height<2)return; GraphicsPath path=new GraphicsPath(); int d=radius*2; path.AddArc(0,0,d,d,180,90); path.AddArc(control.Width-d-1,0,d,d,270,90); path.AddArc(control.Width-d-1,control.Height-d-1,d,d,0,90); path.AddArc(0,control.Height-d-1,d,d,90,90); path.CloseFigure(); Region old=control.Region; control.Region=new Region(path); if(old!=null)old.Dispose(); path.Dispose(); }; control.Resize+=delegate { apply(); }; if(control.IsHandleCreated)apply(); else control.HandleCreated+=delegate { apply(); }; }
    void StyleInputs(Control root) { foreach(Control control in root.Controls) { if(control is ComboBox || control is NumericUpDown || control is TextBox) { control.BackColor=Color.FromArgb(250,249,244); Round(control,7); } if(control.HasChildren)StyleInputs(control); } }
    void DrawTab(object sender,DrawItemEventArgs e) { Rectangle r=e.Bounds; r.Inflate(-4,-3); bool selected=e.Index==tabs.SelectedIndex; using(GraphicsPath path=new GraphicsPath()) { int d=14; path.AddArc(r.X,r.Y,d,d,180,90); path.AddArc(r.Right-d,r.Y,d,d,270,90); path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.X,r.Bottom-d,d,d,90,90); path.CloseFigure(); using(SolidBrush brush=new SolidBrush(selected?green:Color.FromArgb(224,235,225)))e.Graphics.FillPath(brush,path); } TextRenderer.DrawText(e.Graphics,tabs.TabPages[e.Index].Text,new Font("Segoe UI Semibold",9),r,selected?Color.White:ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix); }
    void ResetWeekPicker() {
        DateTime monday=Settings.Monday(DateTime.Now);
        weekPicker.Items.Clear();
        weekPicker.Items.Add("This week: "+monday.ToString("MMM d, yyyy")+" - "+monday.AddDays(6).ToString("MMM d, yyyy"));
        for(int i=0;i<5;i++) { DateTime start=monday.AddDays(7*i); weekPicker.Items.Add((i==0?"This week: ":i==1?"Next week: ":"Week of ")+start.ToString("MMM d, yyyy")+" - "+start.AddDays(6).ToString("MMM d, yyyy")); }
        weekPicker.SelectedIndex=0;
    }
    void LoadWeekEditor() {
        WeeklyPlan week=state.GetWeek(selectedWeek);
        for(int i=0;i<7;i++) { int total=week==null?0:week.Minutes[i]; planHours[i].Value=total/60; planMinutes[i].Value=total%60; }
        weekHint.Text=week==null ? "No plan yet. Choose hours and minutes for each day, then save this week's plan." : "Saved for this week only. Edit anytime. Minutes are limited to 0-59.";
        planMode.SelectedIndex=state.AdvancedPlan?1:0; blockEditor.Text=""; foreach(AdvancedBlock block in state.Blocks.FindAll(b=>b.WeekStart==Settings.Monday(selectedWeek).ToString("yyyy-MM-dd")))blockEditor.AppendText(new[]{"Sun","Mon","Tue","Wed","Thu","Fri","Sat"}[block.Day]+" | "+block.Start+" | "+block.End+" | "+block.Activity+Environment.NewLine);
    }
    void SaveBlocks() { string key=Settings.Monday(selectedWeek).ToString("yyyy-MM-dd"); state.Blocks.RemoveAll(b=>b.WeekStart==key); foreach(string raw in blockEditor.Lines) { string[] p=raw.Split('|'); if(p.Length<4)continue; int dayIndex=Array.IndexOf(new[]{"Sun","Mon","Tue","Wed","Thu","Fri","Sat"},p[0].Trim()); TimeSpan start,end; if(dayIndex<0 || !TimeSpan.TryParse(p[1].Trim(),out start) || !TimeSpan.TryParse(p[2].Trim(),out end) || end<=start)continue; state.Blocks.Add(new AdvancedBlock { WeekStart=key,Day=dayIndex,Start=start.ToString(@"hh\:mm"),End=end.ToString(@"hh\:mm"),Activity=p[3].Trim() }); } }
    string FormatPlan(int[] values) { return FormatDuration(values[0]*60)+" Mon · "+FormatDuration(values[1]*60)+" Tue · "+FormatDuration(values[2]*60)+" Wed · "+FormatDuration(values[3]*60)+" Thu · "+FormatDuration(values[4]*60)+" Fri · "+FormatDuration(values[5]*60)+" Sat · "+FormatDuration(values[6]*60)+" Sun;"; }
    void PromptForWeek() {
        string key=Settings.Monday(DateTime.Now).ToString("yyyy-MM-dd");
        if(HasPlan || promptedWeek==key)return;
        promptedWeek=key; ResetWeekPicker(); tabs.SelectedIndex=0;
        Notify("Let's plan your week", "Choose your daily minutes for "+Settings.Monday(DateTime.Now).ToString("MMM d")+" onward. Tracking continues while you make your plan.");
    }
    void SessionChanged(object sender,SessionSwitchEventArgs e) {
        if(IsDisposed || !IsHandleCreated)return;
        BeginInvoke((MethodInvoker)delegate {
            if(e.Reason==SessionSwitchReason.SessionLock || e.Reason==SessionSwitchReason.ConsoleDisconnect || e.Reason==SessionSwitchReason.RemoteDisconnect)sessionLocked=true;
            if(e.Reason==SessionSwitchReason.SessionUnlock || e.Reason==SessionSwitchReason.ConsoleConnect || e.Reason==SessionSwitchReason.RemoteConnect)sessionLocked=false;
            last=watch.Elapsed.TotalSeconds;
        });
    }
    void PowerChanged(object sender,PowerModeChangedEventArgs e) {
        if(IsDisposed || !IsHandleCreated)return;
        BeginInvoke((MethodInvoker)delegate { last=watch.Elapsed.TotalSeconds; });
    }
    protected override void Dispose(bool disposing) {
        if(disposing) {
            SystemEvents.SessionSwitch-=SessionChanged; SystemEvents.PowerModeChanged-=PowerChanged;
            if(timer!=null)timer.Dispose(); if(tray!=null)tray.Dispose(); if(toast!=null)toast.Dispose();
            if(cornerBar!=null)cornerBar.Dispose(); if(breakScreen!=null)breakScreen.Dispose(); alertSound.Dispose();
        }
        base.Dispose(disposing);
    }
    Button ButtonAt(Control p,string t,int x,int y,int w,EventHandler a) { return ButtonAt(p,t,x,y,w,34,a); }
    Button ButtonAt(Control p,string t,int x,int y,int w,int h,EventHandler a) { Button b=new Button { Text=t,Location=new Point(x,y),Size=new Size(w,h),FlatStyle=FlatStyle.Flat,BackColor=green,ForeColor=Color.White,Cursor=Cursors.Hand,Font=new Font("Segoe UI Semibold",9) }; b.FlatAppearance.BorderSize=0; b.FlatAppearance.MouseOverBackColor=Color.FromArgb(76,145,121); b.FlatAppearance.MouseDownBackColor=Color.FromArgb(46,105,86); b.Click+=a; p.Controls.Add(b); Round(b,9); return b; }
    void LoadState() {
        state=new Settings();
        if(!File.Exists(StatePath))return;
        try { using(var f=File.OpenRead(StatePath))state=(Settings)new XmlSerializer(typeof(Settings)).Deserialize(f);
            if(state.Weeks==null || state.Weeks.Exists(w=>w==null || w.Minutes==null || w.Minutes.Length!=7 || Array.Exists(w.Minutes,v=>v<0 || v>1440)) || (state.Interval!=15 && state.Interval!=20 && state.Interval!=30) || state.BreakMinutes<1 || state.BreakMinutes>60 || state.Days==null)throw new InvalidDataException();
            foreach(DayRecord savedDay in state.Days) { if(savedDay.Activities==null)savedDay.Activities=new List<ActivityRecord>(); foreach(ActivityRecord savedActivity in savedDay.Activities)if(string.IsNullOrEmpty(savedActivity.Category))savedActivity.Category=ActivityCategoryForLoaded(savedActivity.Name); }
            if(state.PlanChanges==null)state.PlanChanges=new List<PlanChange>();
            state.AlertVolume=Math.Max(0,Math.Min(100,state.AlertVolume));
        } catch { MessageBox.Show("Your saved data could not be read. The original file will be kept as a backup.","Screen Time"); File.Copy(StatePath,StatePath+".backup-"+DateTime.Now.Ticks); state=new Settings(); }
    }
    static string ActivityCategoryForLoaded(string name) {
        string value=(name??"").ToLowerInvariant();
        if(value.Contains("outlook")||value.Contains("teams")||value.Contains("slack")||value.Contains("discord")||value.Contains("gmail")||value.Contains("zoom"))return "Communication";
        if(value.Contains("word")||value.Contains("excel")||value.Contains("powerpoint")||value.Contains("docs")||value.Contains("school")||value.Contains("canvas")||value.Contains("classroom"))return "School / work";
        if(value.Contains("capcut")||value.Contains("photoshop")||value.Contains("premiere")||value.Contains("davinci")||value.Contains("figma")||value.Contains("canva"))return "Creative";
        if(value.Contains("youtube")||value.Contains("netflix")||value.Contains("twitch")||value.Contains("game")||value.Contains("steam")||value.Contains("spotify")||value.Contains("roblox")||value.Contains("minecraft"))return "Entertainment";
        return "Other";
    }
    void Save() { try { Directory.CreateDirectory(folder); string temp=StatePath+".tmp"; using(var f=File.Create(temp))new XmlSerializer(typeof(Settings)).Serialize(f,state); if(File.Exists(StatePath))File.Replace(temp,StatePath,null); else File.Move(temp,StatePath); saveFailed=false; } catch { if(!saveFailed)MessageBox.Show("Could not save your plan and usage. Check available disk space and folder permissions.","Screen Time"); saveFailed=true; } }
    void Today() { string date=DateTime.Now.ToString("yyyy-MM-dd"); if(day!=null && day.Date==date)return; day=state.Days.Find(d=>d.Date==date); if(day==null) { day=new DayRecord { Date=date }; state.Days.Add(day); } }
    void EnsureCurrentWeekPlan() {
        if(state.GetWeek(DateTime.Now)!=null)return;
        WeeklyPlan previous=state.GetWeek(Settings.Monday(DateTime.Now).AddDays(-1));
        if(previous==null)return;
        state.SetWeek(DateTime.Now,(int[])previous.Minutes.Clone());
        state.PlanChanges.Add(new PlanChange { WeekStart=Settings.Monday(DateTime.Now).ToString("yyyy-MM-dd"), ChangedAt=DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Summary="Carried forward from the previous week" });
        Save();
    }
    void Tick() {
        double now=watch.Elapsed.TotalSeconds, elapsed=now-last; last=now;
        // A suspended system must not accrue the elapsed sleep interval.
        if(elapsed>5)elapsed=0;
        string previous=day.Date; Today(); if(previous!=day.Date) { elapsed=0; todayLabel.Text="TODAY  "+DateTime.Now.ToString("dddd, MMM d"); ResetWeekPicker(); }
        AdvancedBlock before=ActiveBlock;
        if(state.AdvancedPlan && state.DailyShutdown && before!=null) { state.DailyShutdown=false; state.BreakOffscreen=false; day.ActiveBlock=""; day.BlockUsed=0; day.Exhausted=false; if(breakScreen!=null)breakScreen.Hide(); Save(); }
        ApplyElapsed(elapsed,sessionLocked);
        if(state.AdvancedPlan && !state.DailyShutdown && before==null && day.ActiveBlock.Length>0)StartDailyShutdown();
        UpdateCleanup();
        TrackForeground(elapsed);
        if(++ticks%15==0)Save(); RefreshView(); if(!sessionLocked)PromptForWeek();
    }
    string ForegroundActivity() {
        IntPtr hwnd=GetForegroundWindow(); if(hwnd==IntPtr.Zero)return "Locked or unavailable";
        uint pid; GetWindowThreadProcessId(hwnd,out pid);
        try {
            Process process=Process.GetProcessById((int)pid);
            if(process.Id==Process.GetCurrentProcess().Id)return "Screen Time";
            StringBuilder title=new StringBuilder(512); GetWindowText(hwnd,title,title.Capacity);
            string name=process.ProcessName;
            if(name.Equals("msedge",StringComparison.OrdinalIgnoreCase) || name.Equals("chrome",StringComparison.OrdinalIgnoreCase) || name.Equals("firefox",StringComparison.OrdinalIgnoreCase) || name.Equals("brave",StringComparison.OrdinalIgnoreCase)) {
                string visible=title.ToString().Trim(); return visible.Length>0 ? visible : name;
            }
            if(name.Equals("teams",StringComparison.OrdinalIgnoreCase))return "Microsoft Teams";
            if(name.Equals("outlook",StringComparison.OrdinalIgnoreCase))return "Outlook";
            if(name.Equals("capcut",StringComparison.OrdinalIgnoreCase))return "CapCut";
            string window=title.ToString().Trim(); return window.Length>0 ? process.ProcessName+" - "+window : process.ProcessName;
        } catch { return "Other app"; }
    }
    void TrackForeground(double elapsed) {
        if(sessionLocked || elapsed<=0)return;
        string current=ForegroundActivity();
        if(current=="Screen Time")return;
        if(foregroundName.Length==0)foregroundName=current;
        if(current!=foregroundName) { AddActivity(foregroundName,foregroundSince); foregroundName=current; foregroundSince=0; }
        foregroundSince+=elapsed;
    }
    string ActivityCategory(string name) {
        string value=name.ToLowerInvariant();
        if(value.Contains("outlook") || value.Contains("teams") || value.Contains("slack") || value.Contains("discord") || value.Contains("gmail") || value.Contains("zoom"))return "Communication";
        if(value.Contains("word") || value.Contains("excel") || value.Contains("powerpoint") || value.Contains("docs") || value.Contains("school") || value.Contains("canvas") || value.Contains("classroom"))return "School / work";
        if(value.Contains("capcut") || value.Contains("photoshop") || value.Contains("premiere") || value.Contains("davinci") || value.Contains("figma") || value.Contains("canva"))return "Creative";
        if(value.Contains("youtube") || value.Contains("netflix") || value.Contains("twitch") || value.Contains("game") || value.Contains("steam") || value.Contains("spotify") || value.Contains("roblox") || value.Contains("minecraft"))return "Entertainment";
        return "Other";
    }
    void AddActivity(string name,double seconds) {
        if(string.IsNullOrWhiteSpace(name) || seconds<=0 || name=="Screen Time")return;
        ActivityRecord record=day.Activities.Find(a=>a.Name==name); if(record==null) { record=new ActivityRecord { Name=name, Category=ActivityCategory(name) }; day.Activities.Add(record); } record.Seconds+=seconds;
    }
    static string ActivityDuration(double seconds) { return FormatDuration(seconds); }
    void RefreshActivities() {
        if(activityList==null)return;
        AddActivity(foregroundName,foregroundSince); foregroundSince=0;
        activityList.BeginUpdate(); activityList.Items.Clear();
        Dictionary<string,double> categories=new Dictionary<string,double>(); foreach(ActivityRecord a in day.Activities) { if(!categories.ContainsKey(a.Category))categories[a.Category]=0; categories[a.Category]+=a.Seconds; }
        List<KeyValuePair<string,double>> categoryList=new List<KeyValuePair<string,double>>(categories); categoryList.Sort(delegate(KeyValuePair<string,double> a,KeyValuePair<string,double> b) { return b.Value.CompareTo(a.Value); });
        foreach(KeyValuePair<string,double> category in categoryList)activityList.Items.Add(category.Key+": "+ActivityDuration(category.Value));
        if(categoryList.Count>0)activityList.Items.Add("------------------------------");
        List<ActivityRecord> sorted=new List<ActivityRecord>(day.Activities); sorted.Sort(delegate(ActivityRecord a,ActivityRecord b) { return b.Seconds.CompareTo(a.Seconds); });
        foreach(ActivityRecord a in sorted)if(a.Seconds>=1)activityList.Items.Add(a.Category+"  ·  "+a.Name+": "+ActivityDuration(a.Seconds));
        if(activityList.Items.Count==0)activityList.Items.Add("No foreground activity recorded yet.");
        activityList.EndUpdate();
        activityChart.SetData(day.Activities);
    }
    void ApplyElapsed(double elapsed,bool locked) {
        bool breakFinished=state.BreakUntil!=DateTime.MinValue && !Breaking;
        if(breakFinished) {
            state.BreakUntil=DateTime.MinValue; state.BreakWaiting=true; day.BreakEarned=true; day.SinceReminder=0; Save();
            if(breakScreen!=null)breakScreen.ShowFinished();
        }
        if(!locked && !Breaking && !state.BreakWaiting && !state.DailyShutdown) {
            AdvancedBlock active=ActiveBlock;
            if(state.AdvancedPlan && active!=null) { string key=active.WeekStart+":"+active.Day+":"+active.Start; if(key!=day.ActiveBlock) { day.ActiveBlock=key; day.BlockUsed=0; day.Warned=false; } day.BlockUsed+=elapsed; } else if(!state.AdvancedPlan) day.Used+=elapsed;
            if(!cleanupActive)day.SinceReminder+=elapsed;
            double used=state.AdvancedPlan?day.BlockUsed:day.Used; double left=Budget-used;
            if(state.AdvancedPlan && active!=null && !cleanupActive && !day.Warned && (DateTime.Today.Add(TimeSpan.Parse(active.End))-DateTime.Now).TotalSeconds<=60) { day.Warned=true; cleanupClosesBlock=true; BeginCleanup(); }
            if(HasPlan && left<=0 && !day.Exhausted) { day.Exhausted=true; day.BreakEarned=false; StartDailyShutdown(); }
            else if(HasPlan && left>0 && left<=600 && !day.Warned) { day.Warned=true; Notify("Time to wrap up", "You have "+Math.Ceiling(left/60)+" minutes left in your plan today. Find a good stopping point."); }
            else if(!Breaking && !cleanupActive && day.SinceReminder>=state.Interval*60) { day.SinceReminder=0; BeginCleanup(); }
        }
        if(breakFinished) { day.BreakEarned=true; day.SinceReminder=0; Notify("Welcome back", "Your break timer is complete. Tracking is automatic. Add time with a reason if you need it."); Save(); }
    }
    void BeginCleanup() {
        cleanupActive=true; cleanupUntil=DateTime.UtcNow.AddSeconds(60);
        cleanupLastPing=60;
        if(toast!=null)toast.Close();
        toast=new Reminder { Text="Wrap up",ClientSize=new Size(438,174),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,TopMost=true,ShowInTaskbar=false,BackColor=Color.FromArgb(239,246,239) };
        AddLabel(toast,"TIME TO WRAP UP",16,12,406,32,15);
        AddLabel(toast,"Finish what you’re doing, then a break will begin automatically.",16,52,406,40,11);
        cleanupLabel=AddLabel(toast,"Cleanup time: 1:00",16,92,406,25,13);
        ButtonAt(toast,"Cancel break",122,126,194,delegate { CancelCleanup(); });
        toast.FormClosed+=delegate { if(cleanupTimer!=null)cleanupTimer.Stop(); alertSound.Stop(); };
        alertSound.Play(state.AlertVolume); toast.Show(); PositionReminder();
        cleanupTimer=new System.Windows.Forms.Timer { Interval=250 }; cleanupTimer.Tick+=delegate { UpdateCleanup(); }; cleanupTimer.Start();
    }
    void UpdateCleanup() {
        if(!cleanupActive)return;
        double left=(cleanupUntil-DateTime.UtcNow).TotalSeconds;
        if(left<=0) { cleanupActive=false; if(cleanupTimer!=null)cleanupTimer.Stop(); if(toast!=null)toast.Close(); if(cleanupClosesBlock) { cleanupClosesBlock=false; StartDailyShutdown(); } else StartBreak(); return; }
        int whole=(int)Math.Ceiling(left); if((whole==30 || whole==10) && cleanupLastPing!=whole) { cleanupLastPing=whole; alertSound.Play(state.AlertVolume); }
        if(cleanupLabel!=null && !cleanupLabel.IsDisposed)cleanupLabel.Text="Cleanup time: "+CornerBar.Countdown(left);
        PositionReminder();
    }
    void CancelCleanup() { cleanupActive=false; cleanupClosesBlock=false; if(cleanupTimer!=null)cleanupTimer.Stop(); if(toast!=null)toast.Close(); last=watch.Elapsed.TotalSeconds; Save(); RefreshView(); }
    void StartBreak() {
        if(Breaking || state.BreakWaiting)return;
        state.BreakUntil=DateTime.UtcNow.AddMinutes(state.BreakMinutes); state.BreakWaiting=false; state.BreakOffscreen=false; day.SinceReminder=0; Save();
        if(breakScreen==null || breakScreen.IsDisposed)breakScreen=new BreakScreen(CancelBreak,ContinueBreak,OffscreenActivity,AddTime);
        breakScreen.ShowBreak(state.BreakUntil); RefreshView();
    }
    void StartDailyShutdown() {
        state.DailyShutdown=true; state.BreakWaiting=false; state.BreakOffscreen=true; state.BreakUntil=DateTime.MinValue; Save();
        if(breakScreen==null || breakScreen.IsDisposed)breakScreen=new BreakScreen(CancelBreak,ContinueBreak,OffscreenActivity,AddTime);
        DateTime? next=state.AdvancedPlan?NextBlockStart:null; string message=next==null?"Your daily screen-time plan is complete. Add more time if you have a reason.":"This block is complete. Your next block starts "+next.Value.ToString("ddd h:mm tt")+".";
        breakScreen.ShowDailyShutdown(message); RefreshView();
    }
    void CancelBreak() { if(!Breaking && !state.BreakWaiting && !state.BreakOffscreen)return; state.BreakUntil=DateTime.MinValue; state.BreakWaiting=false; state.BreakOffscreen=false; if(breakScreen!=null)breakScreen.Hide(); last=watch.Elapsed.TotalSeconds; Save(); RefreshView(); }
    void ContinueBreak() { if(!state.BreakWaiting && !state.BreakOffscreen)return; state.BreakWaiting=false; state.BreakOffscreen=false; if(breakScreen!=null)breakScreen.Hide(); last=watch.Elapsed.TotalSeconds; Save(); RefreshView(); }
    void OffscreenActivity() { state.BreakOffscreen=true; state.BreakWaiting=true; state.BreakUntil=DateTime.MinValue; if(breakScreen!=null)breakScreen.SetOffscreen(); Save(); RefreshView(); }
    void AddTime() {
        bool restoreDailyShutdown=state.DailyShutdown;
        if(restoreDailyShutdown && breakScreen!=null)breakScreen.Hide();
        if(!restoreDailyShutdown) { Show(); WindowState=FormWindowState.Normal; Activate(); }
        if(!HasPlan) { tabs.SelectedIndex=0; weekPicker.SelectedIndex=0; MessageBox.Show("Make a plan for this week first. Your usage is already being tracked.","Plan your week"); return; }
        if(Breaking || (day.Used>=Budget && !day.BreakEarned && !state.DailyShutdown)) { MessageBox.Show("Take your "+state.BreakMinutes+" minute break first, then come back to add time.","A little breathing room"); return; }
        using(Form dialog=new Form { Text="More time, with intention",ClientSize=new Size(470,270),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Font=Font }) {
            if(restoreDailyShutdown) { dialog.FormBorderStyle=FormBorderStyle.None; dialog.WindowState=FormWindowState.Maximized; dialog.TopMost=true; dialog.ShowInTaskbar=false; dialog.StartPosition=FormStartPosition.CenterScreen; dialog.BackColor=Color.FromArgb(18,38,34); }
            AddLabel(dialog,"How much more time do you need?",20,18,430,30,13);
            NumericUpDown amount=new NumericUpDown { Location=new Point(20,56),Minimum=1,Maximum=240,Value=15,Size=new Size(90,30) }; dialog.Controls.Add(amount); AddLabel(dialog,"minutes",122,60,200,28,10);
            AddLabel(dialog,"What would you like to finish? A reason is required.",20,102,430,28,10);
            TextBox reason=new TextBox { Location=new Point(20,136),Size=new Size(430,60),Multiline=true,MaxLength=500 }; dialog.Controls.Add(reason);
            ButtonAt(dialog,"Add time",300,214,150,delegate { if(string.IsNullOrWhiteSpace(reason.Text)) { MessageBox.Show(dialog,"Please write a reason first."); return; } Today(); if(Breaking || (day.Used>=Budget && !day.BreakEarned && !state.DailyShutdown)) { MessageBox.Show(dialog,"Your plan has ended. Take a break before adding time."); dialog.Close(); return; } day.Extra+=(int)amount.Value; day.Reasons.Add(DateTime.Now.ToString("HH:mm")+"  +"+amount.Value+" min  "+reason.Text.Trim().Replace("\r"," ").Replace("\n"," ")); day.Warned=false; day.Exhausted=false; day.BreakEarned=false; state.DailyShutdown=false; state.BreakOffscreen=false; state.BreakWaiting=false; if(breakScreen!=null)breakScreen.Hide(); last=watch.Elapsed.TotalSeconds; Save(); dialog.DialogResult=DialogResult.OK; });
            if(restoreDailyShutdown)foreach(Control control in dialog.Controls)if(control is Label)control.ForeColor=Color.White;
            dialog.ShowDialog(this);
            if(restoreDailyShutdown && state.DailyShutdown && breakScreen!=null)breakScreen.ShowDailyShutdown();
            RefreshView();
        }
    }
    void RefreshView() {
        double left=Budget-day.Used;
        remaining.Text=!HasPlan ? "Let's plan this week" : left>=0 ? FormatDuration(left)+" remaining" : FormatDuration(-left)+" beyond your plan";
        detail.Text=FormatDuration(day.Used)+" used"+(!HasPlan?"  /  No plan saved for this week": "  /  "+FormatDuration(Budget)+" planned")+(day.Extra>0 ? "  (includes "+FormatDuration(day.Extra*60)+" extra)" : "");
        progress.Value=!HasPlan?0:Budget<=0?1000:(int)Math.Max(0,Math.Min(1000,day.Used/Budget*1000));
        status.Text=Breaking || state.BreakWaiting ? "FULL-SCREEN BREAK - Continue when you are ready." : "AUTO TRACKING - Unlocked time counts. Lock with Win+L when you step away.";
        rest.Enabled=!Breaking && !state.BreakWaiting; extra.Enabled=!Breaking && !state.BreakWaiting && HasPlan;
        UpdateCorner();
        if(history!=null && ticks%5==0) { history.BeginUpdate(); history.Items.Clear(); for(int i=state.Days.Count-1;i>=0;i--) { var d=state.Days[i]; history.Items.Add(d.Date+"    "+FormatDuration(d.Used)+" used    "+FormatDuration(d.Extra*60)+" extra"); foreach(string r in d.Reasons)history.Items.Add("    "+r); } history.EndUpdate(); RefreshActivities(); }
    }
    static string FormatDuration(double seconds) { int min=(int)Math.Floor(Math.Max(0,seconds)/60); int hours=min/60, minutes=min%60; if(hours==0 && minutes==0)return "0m"; if(hours==0)return minutes+"m"; if(minutes==0)return hours+"h"; return hours+"h "+minutes+"m"; }
    void UpdateCorner() {
        if(cornerBar==null || cornerBar.IsDisposed)return;
        double used=state.AdvancedPlan?day.BlockUsed:day.Used;
        double periodic=Math.Max(0,state.Interval*60-day.SinceReminder), closing=Math.Max(0,Budget-used);
        bool closingSoon=HasPlan && closing<=periodic;
        double next=Breaking?(state.BreakUntil-DateTime.UtcNow).TotalSeconds:(closingSoon?closing:periodic);
        cornerBar.UpdateStatus(HasPlan,Budget,used,next,Breaking||state.BreakWaiting,closingSoon);
        cornerBar.PlaceInCorner(Screen.PrimaryScreen.WorkingArea);
        cornerBar.SetDashboardOpen(Visible);
        if(cornerStarted && !cornerBar.Visible)cornerBar.Show();
        PositionReminder();
    }
    void PositionReminder() {
        if(toast==null || toast.IsDisposed || cornerBar==null)return;
        Rectangle area=Screen.PrimaryScreen.WorkingArea;
        toast.Location=new Point(Math.Max(area.Left,cornerBar.Right-toast.Width),Math.Max(area.Top,cornerBar.Top-toast.Height-10));
    }
    void Notify(string title,string message) {
        if(toast!=null)toast.Close();
        alertSound.Play(state.AlertVolume);
        toast=new Reminder { Text=title,ClientSize=new Size(438,174),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,TopMost=true,ShowInTaskbar=false,BackColor=Color.FromArgb(239,246,239) };
        UpdateCorner(); PositionReminder();
        AddLabel(toast,title,16,12,406,32,15); AddLabel(toast,message,16,52,406,66,11);
        ButtonAt(toast,"Start break",122,126,194,delegate { StartBreak(); toast.Close(); }); toast.FormClosed+=delegate { alertSound.Stop(); }; toast.Show();
        Form current=toast; var dismiss=new System.Windows.Forms.Timer { Interval=30000 }; dismiss.Tick+=delegate { dismiss.Stop(); if(!current.IsDisposed)current.Close(); dismiss.Dispose(); }; dismiss.Start();
    }
    class Reminder : Form {
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams p=base.CreateParams; p.ExStyle|=0x08000000|0x00000080; return p; } }
    }
    [STAThread] public static void Main(string[] args) { bool created; using(var mutex=new Mutex(true,"Local\\TrustScreenTime",out created)) { if(!created) { MessageBox.Show("Screen Time is already running. Open it from the system tray."); return; } Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new ScreenTime()); } }
}


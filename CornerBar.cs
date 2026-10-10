using System;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

public sealed class PauseOverlay : Control {
    Bitmap snapshot;
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window,IntPtr deviceContext,uint flags);
    public event EventHandler ResumeRequested;
    public PauseOverlay() {
        Dock=DockStyle.Fill; Visible=false; Cursor=Cursors.Hand; TabStop=true;
        AccessibleName="Pace is paused. Click anywhere to resume."; AccessibleRole=AccessibleRole.PushButton;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        MouseDown+=delegate { if(ResumeRequested!=null)ResumeRequested(this,EventArgs.Empty); };
    }
    public void Cover(Control parent) {
        Visible=false;
        if(snapshot!=null) { snapshot.Dispose(); snapshot=null; }
        if(parent.ClientSize.Width>0 && parent.ClientSize.Height>0) {
            snapshot=new Bitmap(parent.ClientSize.Width,parent.ClientSize.Height);
            bool printed=false;
            if(parent.IsHandleCreated)using(Graphics graphics=Graphics.FromImage(snapshot)) { IntPtr context=graphics.GetHdc(); try { printed=PrintWindow(parent.Handle,context,1); } finally { graphics.ReleaseHdc(context); } }
            if(!printed)parent.DrawToBitmap(snapshot,new Rectangle(Point.Empty,parent.ClientSize));
        }
        Visible=true; BringToFront(); Focus(); Invalidate();
    }
    public void Uncover() { Visible=false; if(snapshot!=null) { snapshot.Dispose(); snapshot=null; } }
    protected override void OnPaint(PaintEventArgs e) {
        if(snapshot!=null)e.Graphics.DrawImageUnscaled(snapshot,Point.Empty);
        using(SolidBrush dim=new SolidBrush(Color.FromArgb(105,35,39,38)))e.Graphics.FillRectangle(dim,ClientRectangle);
        float size=Math.Max(38,Math.Min(Width,Height)*.48f);
        using(Font font=new Font("Segoe UI Symbol",size,FontStyle.Bold,GraphicsUnit.Pixel))
        using(SolidBrush symbol=new SolidBrush(Color.FromArgb(150,205,210,208))) {
            using(StringFormat format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center })e.Graphics.DrawString("\u23F8",font,symbol,ClientRectangle,format);
        }
    }
    protected override void Dispose(bool disposing) { if(disposing && snapshot!=null) { snapshot.Dispose(); snapshot=null; } base.Dispose(disposing); }
}

// A separate, unowned window stays visible when the dashboard is hidden/minimized.
public sealed class CornerBar : Form {
    public bool IsMinimized { get; private set; }
    bool userPositioned;
    bool changingWindowMode;
    bool showingReason;
    bool noticeWarning;
    bool doNotDisturbActive;
    string noticePrefix="";
    readonly Label allotted, used, next, nextTitle, heading, allottedTitle, usedTitle, reason,trackingHealth;
    readonly Button addTime, takeBreak;
    readonly Button endDay;
    readonly Button pause;
    readonly Button doNotDisturb;
    readonly Button minimize;
    readonly Button resetPosition;
    readonly ProgressBar progress;
    readonly PauseOverlay pauseOverlay;
    public event EventHandler OpenDashboard;
    public event EventHandler AddTimeClicked;
    public event EventHandler TakeBreakClicked;
    public event EventHandler EndDayClicked;
    public event EventHandler PauseClicked;
    public event EventHandler DoNotDisturbClicked;
    public event EventHandler UserPositionChanged;
    public event EventHandler PositionReset;
    public CornerBar() {
        Text="Pace"; AccessibleName="Pace screen-time bar"; Icon=SystemIcons.Application;
        AutoScaleMode=AutoScaleMode.Dpi; ClientSize=new Size(438,112);
        FormBorderStyle=FormBorderStyle.None; StartPosition=FormStartPosition.Manual;
        ShowInTaskbar=false; TopMost=true; BackColor=Color.FromArgb(35,67,58);
        Font=new Font("Segoe UI",10); Cursor=Cursors.SizeAll;
        heading=LabelAt("PACE  /  Click to open",16,9,170,18,9);
        heading.Cursor=Cursors.Hand; heading.AccessibleRole=AccessibleRole.PushButton; heading.AccessibleName="Open or close Pace"; heading.Click+=Open;
        endDay=ActionButton("End day",190,7,58,22); endDay.Click+=delegate { if(EndDayClicked!=null)EndDayClicked(this,EventArgs.Empty); };
        pause=ActionButton("\u23F8",252,7,28,22); pause.AccessibleName="Pause Pace"; pause.Font=new Font("Segoe UI Symbol",9); pause.Click+=delegate { if(PauseClicked!=null)PauseClicked(this,EventArgs.Empty); };
        doNotDisturb=ActionButton("",284,7,28,22); doNotDisturb.AccessibleName="Turn on do not disturb"; doNotDisturb.Paint+=DrawDoNotDisturb; doNotDisturb.Click+=delegate { if(DoNotDisturbClicked!=null)DoNotDisturbClicked(this,EventArgs.Empty); };
        trackingHealth=LabelAt("● ACTIVE",316,9,58,18,7.5f); trackingHealth.TextAlign=ContentAlignment.MiddleRight; trackingHealth.ForeColor=Color.FromArgb(179,224,189);
        minimize=ActionButton("–",402,7,20,20); minimize.Click+=delegate { MinimizeToTaskbar(); };
        resetPosition=ActionButton("⌂",378,7,20,20); resetPosition.Font=new Font("Segoe UI Symbol",10); resetPosition.Click+=delegate { ResetPosition(); };
        reason=LabelAt("",16,34,406,50,13.5f); reason.Visible=false; reason.TextAlign=ContentAlignment.MiddleCenter; reason.BackColor=Color.FromArgb(48,85,72);
        allottedTitle=LabelAt("ALLOTTED",16,34,75,18,8);
        usedTitle=LabelAt("USED",156,34,130,18,8);
        nextTitle=LabelAt("NEXT BREAK",296,34,70,18,8);
        allotted=LabelAt("No plan",16,54,130,31,16);
        addTime=ActionButton("+ Time",94,30,52,22); addTime.Click+=delegate { if(AddTimeClicked!=null)AddTimeClicked(this,EventArgs.Empty); };
        used=LabelAt("0 min",156,54,130,31,16);
        next=LabelAt("20:00",296,54,130,31,16);
        takeBreak=ActionButton("Break",370,30,52,22); takeBreak.Click+=delegate { if(TakeBreakClicked!=null)TakeBreakClicked(this,EventArgs.Empty); };
        progress=new ProgressBar { Location=new Point(16,94),Size=new Size(406,5),Maximum=1000 };
        Controls.Add(progress);
        foreach(Control control in new Control[] { trackingHealth,reason,allottedTitle,usedTitle,nextTitle,allotted,used,next,progress })MakeDragSurface(control);
        pauseOverlay=new PauseOverlay(); pauseOverlay.ResumeRequested+=delegate { if(PauseClicked!=null)PauseClicked(this,EventArgs.Empty); }; Controls.Add(pauseOverlay);
        MouseDown+=DragFromEmptySpace;
        FormClosing+=delegate(object s,FormClosingEventArgs e) { if(e.CloseReason==CloseReason.UserClosing)e.Cancel=true; };
        SizeChanged+=delegate { ApplyRoundedRegion(); if(!changingWindowMode && IsMinimized && WindowState==FormWindowState.Normal)RestoreBar(); };
        ApplyRoundedRegion();
        SetDarkMode(PaceTheme.Dark);
    }
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd,int msg,int wParam,int lParam);
    void DragFromEmptySpace(object sender,MouseEventArgs e) { if(e.Button!=MouseButtons.Left)return; userPositioned=true; ReleaseCapture(); SendMessage(Handle,0xA1,2,0); if(UserPositionChanged!=null)UserPositionChanged(this,EventArgs.Empty); }
    void MakeDragSurface(Control control) { control.Cursor=Cursors.SizeAll; control.MouseDown+=DragFromEmptySpace; }
    Label LabelAt(string text,int x,int y,int w,int h,float size) {
        Label label=new Label { Text=text,Location=new Point(x,y),Size=new Size(w,h),ForeColor=Color.FromArgb(240,246,230),Font=new Font("Segoe UI",size),AutoEllipsis=true };
        Controls.Add(label); return label;
    }
    Button ActionButton(string text,int x,int y,int w,int h) { Button button=new Button { Text=text,Location=new Point(x,y),Size=new Size(w,h),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(76,139,113),ForeColor=Color.White,Font=new Font("Segoe UI Semibold",8),Cursor=Cursors.Hand,TabStop=false }; button.FlatAppearance.BorderSize=0; button.FlatAppearance.MouseOverBackColor=Color.FromArgb(93,157,130); Controls.Add(button); Round(button,7); return button; }
    void DrawDoNotDisturb(object sender,PaintEventArgs e) {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(Pen pen=new Pen(Color.White,1.45f))
        using(GraphicsPath bell=new GraphicsPath()) {
            bell.AddArc(8,3,12,12,180,180); bell.AddLine(20,9,20,14); bell.AddLine(20,14,22,16); bell.AddLine(22,16,6,16); bell.AddLine(6,16,8,14); bell.CloseFigure();
            e.Graphics.DrawPath(pen,bell); e.Graphics.DrawArc(pen,11,16,6,4,0,180); e.Graphics.DrawLine(pen,5,3,23,19);
        }
    }
    static void Round(Control control,int radius) { GraphicsPath path=new GraphicsPath(); int d=radius*2; path.AddArc(0,0,d,d,180,90); path.AddArc(control.Width-d-1,0,d,d,270,90); path.AddArc(control.Width-d-1,control.Height-d-1,d,d,0,90); path.AddArc(0,control.Height-d-1,d,d,90,90); path.CloseFigure(); Region old=control.Region; control.Region=new Region(path); if(old!=null)old.Dispose(); path.Dispose(); }
    void ApplyRoundedRegion() { if(Width>20 && Height>20)Round(this,16); }
    void Open(object sender,EventArgs e) { if(OpenDashboard!=null)OpenDashboard(this,EventArgs.Empty); }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { CreateParams p=base.CreateParams; if(IsMinimized) { p.ExStyle&=~0x00000080; p.ExStyle&=~0x08000000; p.ExStyle|=0x00040000; } else p.ExStyle|=0x08000000|0x00000080; return p; } }
    public static string Countdown(double seconds) { int whole=(int)Math.Ceiling(Math.Max(0,seconds)); if(whole>=3600)return (whole/3600)+":"+((whole%3600)/60).ToString("00")+":"+(whole%60).ToString("00"); return (whole/60).ToString("00")+":"+(whole%60).ToString("00"); }
    static string Duration(double seconds) { int minutes=(int)Math.Floor(Math.Max(0,seconds)/60); int hours=minutes/60, rest=minutes%60; if(hours==0 && rest==0)return "0m"; if(hours==0)return rest+"m"; if(rest==0)return hours+"h"; return hours+"h "+rest+"m"; }
    public void UpdateStatus(bool hasPlan,int budgetSeconds,double usedSeconds,double nextSeconds,bool onBreak) {
        UpdateStatus(hasPlan,budgetSeconds,usedSeconds,nextSeconds,onBreak,false);
    }
    public void UpdateStatus(bool hasPlan,int budgetSeconds,double usedSeconds,double nextSeconds,bool onBreak,bool closing) {
        UpdateStatus(hasPlan,budgetSeconds,usedSeconds,nextSeconds,onBreak?"BREAK LEFT":closing?"CLOSES IN":"NEXT BREAK");
    }
    public void UpdateStatus(bool hasPlan,int budgetSeconds,double usedSeconds,double nextSeconds,string timerTitle) {
        allotted.Text=hasPlan?Duration(budgetSeconds):"No plan";
        used.Text=Duration(usedSeconds);
        used.ForeColor=hasPlan && usedSeconds>budgetSeconds ? Color.FromArgb(255,190,125) : Color.FromArgb(240,246,230);
        nextTitle.Text=timerTitle;
        next.Text=Countdown(nextSeconds);
        progress.Value=!hasPlan?0:budgetSeconds<=0?1000:(int)Math.Max(0,Math.Min(1000,usedSeconds/budgetSeconds*1000));
        UpdateAccessibleDescription();
    }
    public void SetReason(string value) { SetNotice(value,"Reason",false); }
    public void SetFocusMismatch(string blockName,string activityName) {
        string block=(blockName??"").Trim(),activity=(activityName??"").Trim();
        if(block.Length==0 || activity.Length==0) { SetNotice("","",false); return; }
        SetNotice("PLANNED: "+block+"\r\n"+activity+" is outside this block","Focus cue",true);
    }
    void SetNotice(string value,string prefix,bool warning) {
        string clean=(value??"").Trim(); bool show=clean.Length>0; string shownPrefix=show?prefix:""; if(showingReason==show && reason.Text==clean && noticePrefix==shownPrefix && noticeWarning==warning)return;
        int oldBottom=Bottom,offset=show?62:0; showingReason=show; noticePrefix=show?prefix:""; reason.Text=clean; reason.Visible=show;
        reason.BackColor=warning?(PaceTheme.Dark?Color.FromArgb(91,70,38):Color.FromArgb(104,84,54)):(PaceTheme.Dark?Color.FromArgb(35,63,53):Color.FromArgb(48,85,72)); reason.ForeColor=warning?Color.FromArgb(255,239,195):Color.FromArgb(240,246,230);
        if(noticeWarning!=warning) { Font oldFont=reason.Font; reason.Font=new Font(warning?"Segoe UI Semibold":"Segoe UI",warning?10.5f:13.5f); oldFont.Dispose(); } noticeWarning=warning;
        allottedTitle.Top=34+offset; usedTitle.Top=34+offset; nextTitle.Top=34+offset;
        addTime.Top=30+offset; takeBreak.Top=30+offset;
        allotted.Top=54+offset; used.Top=54+offset; next.Top=54+offset; progress.Top=94+offset;
        ClientSize=new Size(438,112+offset); if(userPositioned) { Rectangle area=Screen.FromControl(this).WorkingArea; Top=Math.Max(area.Top,Math.Min(oldBottom-Height,area.Bottom-Height)); if(UserPositionChanged!=null)UserPositionChanged(this,EventArgs.Empty); }
        ApplyRoundedRegion();
        UpdateAccessibleDescription();
    }
    void UpdateAccessibleDescription() { AccessibleDescription="Tracking: "+(trackingHealth.Text.Contains("UNSAVED")?"active, but data is not saving":"active")+". "+(showingReason?noticePrefix+": "+reason.Text.Replace("\r\n",". ")+". ":"")+"Allotted: "+allotted.Text+". Used: "+used.Text+". "+nextTitle.Text+": "+next.Text; }
    public void SetTrackingHealth(bool saving) { trackingHealth.Text=saving?"● ACTIVE":"● UNSAVED"; trackingHealth.ForeColor=saving?(PaceTheme.Dark?Color.FromArgb(154,224,185):Color.FromArgb(179,224,189)):Color.FromArgb(255,190,125); UpdateAccessibleDescription(); }
    public void SetDarkMode(bool dark) {
        BackColor=dark?Color.FromArgb(22,38,33):Color.FromArgb(35,67,58);
        foreach(Label label in new[]{allotted,used,next,nextTitle,heading,allottedTitle,usedTitle})label.ForeColor=Color.FromArgb(240,246,230);
        foreach(Button button in new[]{addTime,takeBreak,endDay,pause,doNotDisturb,minimize,resetPosition}) { button.BackColor=dark?Color.FromArgb(49,91,75):Color.FromArgb(76,139,113); button.FlatAppearance.MouseOverBackColor=dark?Color.FromArgb(64,116,96):Color.FromArgb(93,157,130); }
        ApplyDoNotDisturbAppearance();
        reason.BackColor=noticeWarning?(dark?Color.FromArgb(91,70,38):Color.FromArgb(104,84,54)):(dark?Color.FromArgb(35,63,53):Color.FromArgb(48,85,72));
        SetTrackingHealth(!trackingHealth.Text.Contains("UNSAVED")); Invalidate(true);
    }
    public void PlaceInCorner(Rectangle area) { if(!userPositioned)Location=new Point(Math.Max(area.Left,area.Right-Width-16),Math.Max(area.Top,area.Bottom-Height-16)); }
    public void RestoreSavedPosition(int x,int y) { Point requested=new Point(x,y); Rectangle area=Screen.FromPoint(requested).WorkingArea; userPositioned=true; Location=new Point(Math.Max(area.Left,Math.Min(x,area.Right-Width)),Math.Max(area.Top,Math.Min(y,area.Bottom-Height))); }
    public void EnsureVisible() {
        if(IsMinimized)return;
        Rectangle area=Screen.FromRectangle(Bounds).WorkingArea;
        Point safe=new Point(Math.Max(area.Left,Math.Min(Left,area.Right-Width)),Math.Max(area.Top,Math.Min(Top,area.Bottom-Height)));
        if(Location==safe)return;
        Location=safe; if(userPositioned && UserPositionChanged!=null)UserPositionChanged(this,EventArgs.Empty);
    }
    public void MinimizeToTaskbar() {
        changingWindowMode=true; IsMinimized=true;
        // ShowInTaskbar updates the native window style itself. Recreating the handle again can orphan the taskbar button.
        ShowInTaskbar=true; Show(); WindowState=FormWindowState.Minimized; changingWindowMode=false;
    }
    public void RestoreBar() {
        changingWindowMode=true; WindowState=FormWindowState.Normal; IsMinimized=false; ShowInTaskbar=false; changingWindowMode=false;
        Show(); EnsureVisible(); BringToFront();
    }
    public void ResetPosition() { userPositioned=false; PlaceInCorner(Screen.PrimaryScreen.WorkingArea); if(PositionReset!=null)PositionReset(this,EventArgs.Empty); }
    public void SetDashboardOpen(bool open) { heading.Text=open?"PACE  /  Click to close":"PACE  /  Click to open"; }
    public void SetPaused(bool paused) { if(paused)pauseOverlay.Cover(this); else pauseOverlay.Uncover(); }
    public void SetDoNotDisturb(bool active) { doNotDisturbActive=active; doNotDisturb.AccessibleName=active?"Turn off do not disturb":"Turn on do not disturb"; if(active) { nextTitle.Text="DND ON"; next.Text="—"; } ApplyDoNotDisturbAppearance(); }
    void ApplyDoNotDisturbAppearance() { doNotDisturb.BackColor=doNotDisturbActive?(PaceTheme.Dark?Color.FromArgb(105,88,52):Color.FromArgb(151,121,63)):(PaceTheme.Dark?Color.FromArgb(49,91,75):Color.FromArgb(76,139,113)); doNotDisturb.Invalidate(); }
}

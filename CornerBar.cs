using System;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

// A separate, unowned window stays visible when the dashboard is hidden/minimized.
public sealed class CornerBar : Form {
    public bool IsMinimized { get; private set; }
    bool userPositioned;
    bool changingWindowMode;
    readonly Label allotted, used, next, nextTitle, heading;
    readonly Button addTime, takeBreak;
    readonly Button endDay;
    readonly Button minimize;
    readonly Button resetPosition;
    readonly ProgressBar progress;
    public event EventHandler OpenDashboard;
    public event EventHandler AddTimeClicked;
    public event EventHandler TakeBreakClicked;
    public event EventHandler EndDayClicked;
    public CornerBar() {
        Text="Pace"; AccessibleName="Pace screen-time bar"; Icon=SystemIcons.Application;
        AutoScaleMode=AutoScaleMode.Dpi; ClientSize=new Size(438,112);
        FormBorderStyle=FormBorderStyle.None; StartPosition=FormStartPosition.Manual;
        ShowInTaskbar=false; TopMost=true; BackColor=Color.FromArgb(35,67,58);
        Font=new Font("Segoe UI",10); Cursor=Cursors.Hand;
        heading=LabelAt("PACE  /  Click to open",16,9,195,18,9);
        endDay=ActionButton("End day",220,7,58,22); endDay.Click+=delegate { if(EndDayClicked!=null)EndDayClicked(this,EventArgs.Empty); };
        minimize=ActionButton("–",402,7,20,20); minimize.Click+=delegate { changingWindowMode=true; IsMinimized=true; ShowInTaskbar=true; RecreateHandle(); WindowState=FormWindowState.Minimized; changingWindowMode=false; };
        resetPosition=ActionButton("⌂",378,7,20,20); resetPosition.Font=new Font("Segoe UI Symbol",10); resetPosition.Click+=delegate { ResetPosition(); };
        LabelAt("ALLOTTED",16,34,75,18,8);
        LabelAt("USED",156,34,130,18,8);
        nextTitle=LabelAt("NEXT BREAK",296,34,70,18,8);
        allotted=LabelAt("No plan",16,54,130,31,16);
        addTime=ActionButton("+ Time",94,30,52,22); addTime.Click+=delegate { if(AddTimeClicked!=null)AddTimeClicked(this,EventArgs.Empty); };
        used=LabelAt("0 min",156,54,130,31,16);
        next=LabelAt("20:00",296,54,130,31,16);
        takeBreak=ActionButton("Break",370,30,52,22); takeBreak.Click+=delegate { if(TakeBreakClicked!=null)TakeBreakClicked(this,EventArgs.Empty); };
        progress=new ProgressBar { Location=new Point(16,94),Size=new Size(406,5),Maximum=1000 };
        Controls.Add(progress);
        foreach(Control control in Controls)control.Click+=Open;
        addTime.Click-=Open; takeBreak.Click-=Open; endDay.Click-=Open; minimize.Click-=Open; resetPosition.Click-=Open;
        Click+=Open;
        MouseDown+=DragFromEmptySpace;
        FormClosing+=delegate(object s,FormClosingEventArgs e) { if(e.CloseReason==CloseReason.UserClosing)e.Cancel=true; };
        SizeChanged+=delegate { if(!changingWindowMode && IsMinimized && WindowState==FormWindowState.Normal) { changingWindowMode=true; IsMinimized=false; ShowInTaskbar=false; RecreateHandle(); changingWindowMode=false; BringToFront(); } };
        ApplyRoundedRegion();
    }
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd,int msg,int wParam,int lParam);
    void DragFromEmptySpace(object sender,MouseEventArgs e) { if(e.Button!=MouseButtons.Left)return; userPositioned=true; ReleaseCapture(); SendMessage(Handle,0xA1,2,0); }
    Label LabelAt(string text,int x,int y,int w,int h,float size) {
        Label label=new Label { Text=text,Location=new Point(x,y),Size=new Size(w,h),ForeColor=Color.FromArgb(240,246,230),Font=new Font("Segoe UI",size),AutoEllipsis=true };
        Controls.Add(label); return label;
    }
    Button ActionButton(string text,int x,int y,int w,int h) { Button button=new Button { Text=text,Location=new Point(x,y),Size=new Size(w,h),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(76,139,113),ForeColor=Color.White,Font=new Font("Segoe UI Semibold",8),Cursor=Cursors.Hand,TabStop=false }; button.FlatAppearance.BorderSize=0; button.FlatAppearance.MouseOverBackColor=Color.FromArgb(93,157,130); Controls.Add(button); Round(button,7); return button; }
    static void Round(Control control,int radius) { GraphicsPath path=new GraphicsPath(); int d=radius*2; path.AddArc(0,0,d,d,180,90); path.AddArc(control.Width-d-1,0,d,d,270,90); path.AddArc(control.Width-d-1,control.Height-d-1,d,d,0,90); path.AddArc(0,control.Height-d-1,d,d,90,90); path.CloseFigure(); Region old=control.Region; control.Region=new Region(path); if(old!=null)old.Dispose(); path.Dispose(); }
    void ApplyRoundedRegion() { if(Width>20 && Height>20)Round(this,16); }
    void Open(object sender,EventArgs e) { if(OpenDashboard!=null)OpenDashboard(this,EventArgs.Empty); }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { CreateParams p=base.CreateParams; if(IsMinimized) { p.ExStyle&=~0x00000080; p.ExStyle&=~0x08000000; p.ExStyle|=0x00040000; } else p.ExStyle|=0x08000000|0x00000080; return p; } }
    public static string Countdown(double seconds) { int whole=(int)Math.Ceiling(Math.Max(0,seconds)); return (whole/60).ToString("00")+":"+(whole%60).ToString("00"); }
    static string Duration(double seconds) { int minutes=(int)Math.Floor(Math.Max(0,seconds)/60); int hours=minutes/60, rest=minutes%60; if(hours==0 && rest==0)return "0m"; if(hours==0)return rest+"m"; if(rest==0)return hours+"h"; return hours+"h "+rest+"m"; }
    public void UpdateStatus(bool hasPlan,int budgetSeconds,double usedSeconds,double nextSeconds,bool onBreak) {
        UpdateStatus(hasPlan,budgetSeconds,usedSeconds,nextSeconds,onBreak,false);
    }
    public void UpdateStatus(bool hasPlan,int budgetSeconds,double usedSeconds,double nextSeconds,bool onBreak,bool closing) {
        allotted.Text=hasPlan?Duration(budgetSeconds):"No plan";
        used.Text=Duration(usedSeconds);
        used.ForeColor=hasPlan && usedSeconds>budgetSeconds ? Color.FromArgb(255,190,125) : Color.FromArgb(240,246,230);
        nextTitle.Text=onBreak?"BREAK LEFT":closing?"CLOSES IN":"NEXT BREAK";
        next.Text=Countdown(nextSeconds);
        progress.Value=!hasPlan?0:budgetSeconds<=0?1000:(int)Math.Max(0,Math.Min(1000,usedSeconds/budgetSeconds*1000));
        AccessibleDescription="Allotted: "+allotted.Text+". Used: "+used.Text+". "+nextTitle.Text+": "+next.Text;
    }
    public void PlaceInCorner(Rectangle area) { if(!userPositioned)Location=new Point(Math.Max(area.Left,area.Right-Width-16),Math.Max(area.Top,area.Bottom-Height-16)); }
    public void RestoreBar() { changingWindowMode=true; IsMinimized=false; WindowState=FormWindowState.Normal; ShowInTaskbar=false; RecreateHandle(); changingWindowMode=false; Show(); BringToFront(); }
    public void ResetPosition() { userPositioned=false; PlaceInCorner(Screen.FromControl(this).WorkingArea); }
    public void SetDashboardOpen(bool open) { heading.Text=open?"PACE  /  Click to close":"PACE  /  Click to open"; }
}

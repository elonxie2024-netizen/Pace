using System;
using System.Drawing;
using System.Windows.Forms;

// A separate, unowned window stays visible when the dashboard is hidden/minimized.
public sealed class CornerBar : Form {
    readonly Label allotted, used, next, nextTitle, heading;
    readonly Button addTime, takeBreak;
    readonly Button endDay;
    readonly ProgressBar progress;
    public event EventHandler OpenDashboard;
    public event EventHandler AddTimeClicked;
    public event EventHandler TakeBreakClicked;
    public event EventHandler EndDayClicked;
    public CornerBar() {
        Text="Screen Time status"; AccessibleName=Text;
        AutoScaleMode=AutoScaleMode.Dpi; ClientSize=new Size(438,112);
        FormBorderStyle=FormBorderStyle.None; StartPosition=FormStartPosition.Manual;
        ShowInTaskbar=false; TopMost=true; BackColor=Color.FromArgb(27,57,49);
        Font=new Font("Segoe UI",10); Cursor=Cursors.Hand;
        heading=LabelAt("SCREEN TIME  /  Click to open",16,9,406,18,9);
        endDay=ActionButton("End day",220,7,58,22); endDay.Click+=delegate { if(EndDayClicked!=null)EndDayClicked(this,EventArgs.Empty); };
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
        addTime.Click-=Open; takeBreak.Click-=Open; endDay.Click-=Open;
        Click+=Open;
        FormClosing+=delegate(object s,FormClosingEventArgs e) { if(e.CloseReason==CloseReason.UserClosing)e.Cancel=true; };
    }
    Label LabelAt(string text,int x,int y,int w,int h,float size) {
        Label label=new Label { Text=text,Location=new Point(x,y),Size=new Size(w,h),ForeColor=Color.FromArgb(240,246,230),Font=new Font("Segoe UI",size),AutoEllipsis=true };
        Controls.Add(label); return label;
    }
    Button ActionButton(string text,int x,int y,int w,int h) { Button button=new Button { Text=text,Location=new Point(x,y),Size=new Size(w,h),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(38,125,103),ForeColor=Color.White,Font=new Font("Segoe UI",8),Cursor=Cursors.Hand,TabStop=false }; button.FlatAppearance.BorderSize=0; Controls.Add(button); return button; }
    void Open(object sender,EventArgs e) { if(OpenDashboard!=null)OpenDashboard(this,EventArgs.Empty); }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { CreateParams p=base.CreateParams; p.ExStyle|=0x08000000|0x00000080; return p; } }
    public static string Countdown(double seconds) { int whole=(int)Math.Ceiling(Math.Max(0,seconds)); return (whole/60).ToString("00")+":"+(whole%60).ToString("00"); }
    static string Duration(double seconds) { int minutes=(int)Math.Floor(Math.Max(0,seconds)/60); int hours=minutes/60, rest=minutes%60; if(hours==0 && rest==0)return "0m"; if(hours==0)return rest+"m"; if(rest==0)return hours+"h"; return hours+"h "+rest+"m"; }
    public void UpdateStatus(bool hasPlan,int budgetSeconds,double usedSeconds,double nextSeconds,bool onBreak) {
        allotted.Text=hasPlan?Duration(budgetSeconds):"No plan";
        used.Text=Duration(usedSeconds);
        used.ForeColor=hasPlan && usedSeconds>budgetSeconds ? Color.FromArgb(255,190,125) : Color.FromArgb(240,246,230);
        nextTitle.Text=onBreak?"BREAK LEFT":"NEXT BREAK";
        next.Text=Countdown(nextSeconds);
        progress.Value=!hasPlan?0:budgetSeconds<=0?1000:(int)Math.Max(0,Math.Min(1000,usedSeconds/budgetSeconds*1000));
        AccessibleDescription="Allotted: "+allotted.Text+". Used: "+used.Text+". "+nextTitle.Text+": "+next.Text;
    }
    public void PlaceInCorner(Rectangle area) { Location=new Point(Math.Max(area.Left,area.Right-Width-16),Math.Max(area.Top,area.Bottom-Height-16)); }
    public void SetDashboardOpen(bool open) { heading.Text=open?"SCREEN TIME  /  Click to close":"SCREEN TIME  /  Click to open"; }
}

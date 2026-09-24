using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

public static class AdvancedBlockRules {
    public static AdvancedBlock Copy(AdvancedBlock source,string weekStart) {
        return new AdvancedBlock { WeekStart=weekStart,Day=source.Day,Start=source.Start,End=source.End,Activity=source.Activity };
    }
    public static int Minutes(string value) {
        if(string.IsNullOrWhiteSpace(value))return -1;
        string[] parts=value.Split(':'); int hours,minutes;
        if(parts.Length!=2 || !int.TryParse(parts[0],out hours) || !int.TryParse(parts[1],out minutes))return -1;
        if(hours<0 || hours>24 || minutes<0 || minutes>59 || (hours==24 && minutes!=0))return -1;
        return hours*60+minutes;
    }
    public static string Time(int minutes) {
        minutes=Math.Max(0,Math.Min(1440,minutes));
        if(minutes==1440)return "24:00";
        return TimeSpan.FromMinutes(minutes).ToString(@"hh\:mm");
    }
    public static bool Overlaps(AdvancedBlock first,AdvancedBlock second) {
        if(first==null || second==null || first.WeekStart!=second.WeekStart || first.Day!=second.Day)return false;
        return Minutes(first.Start)<Minutes(second.End) && Minutes(second.Start)<Minutes(first.End);
    }
    public static bool HasConflict(IList<AdvancedBlock> blocks,AdvancedBlock candidate,AdvancedBlock ignore) {
        foreach(AdvancedBlock block in blocks)if(!object.ReferenceEquals(block,ignore) && Overlaps(block,candidate))return true;
        return false;
    }
    public static bool IsValid(AdvancedBlock block) {
        if(block==null || block.Day<0 || block.Day>6 || string.IsNullOrWhiteSpace(block.WeekStart) || string.IsNullOrWhiteSpace(block.Activity))return false;
        int start=Minutes(block.Start),end=Minutes(block.End);
        return start>=0 && end>start && end<=1440;
    }
    public static string DayName(int day) { return new[]{"Sun","Mon","Tue","Wed","Thu","Fri","Sat"}[Math.Max(0,Math.Min(6,day))]; }
    public static int DayToRow(int day) { return day==0?6:day-1; }
    public static int RowToDay(int row) { return row==6?0:row+1; }
}

public sealed class AdvancedSchedulePreview : Control {
    readonly List<AdvancedBlock> blocks=new List<AdvancedBlock>();
    readonly Color ink=Color.FromArgb(38,58,53), green=Color.FromArgb(61,128,105), pale=Color.FromArgb(237,243,235), conflict=Color.FromArgb(188,91,76);
    public AdvancedSchedulePreview() { DoubleBuffered=true; BackColor=Color.FromArgb(252,251,247); Font=new Font("Segoe UI Semibold",7.5f); }
    public void SetBlocks(IEnumerable<AdvancedBlock> values) { blocks.Clear(); if(values!=null)blocks.AddRange(values); Invalidate(); }
    GraphicsPath Rounded(Rectangle rect,int radius) { GraphicsPath path=new GraphicsPath(); int d=Math.Min(radius*2,Math.Min(rect.Width,rect.Height)); path.AddArc(rect.X,rect.Y,d,d,180,90); path.AddArc(rect.Right-d,rect.Y,d,d,270,90); path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90); path.AddArc(rect.X,rect.Bottom-d,d,d,90,90); path.CloseFigure(); return path; }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        int labelWidth=30, timelineX=labelWidth+4, timelineWidth=Math.Max(10,Width-timelineX-3), rowHeight=Math.Max(14,(Height-2)/7);
        for(int row=0;row<7;row++) {
            int y=1+row*rowHeight; Rectangle lane=new Rectangle(timelineX,y,timelineWidth,rowHeight-2);
            using(Brush laneBrush=new SolidBrush(row%2==0?pale:Color.FromArgb(246,247,241)))e.Graphics.FillRectangle(laneBrush,lane);
            TextRenderer.DrawText(e.Graphics,AdvancedBlockRules.DayName(AdvancedBlockRules.RowToDay(row)),Font,new Rectangle(0,y,labelWidth,rowHeight),ink,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
            foreach(AdvancedBlock block in blocks)if(AdvancedBlockRules.DayToRow(block.Day)==row) {
                int start=AdvancedBlockRules.Minutes(block.Start), end=AdvancedBlockRules.Minutes(block.End);
                int x=timelineX+(int)Math.Round(start/1440.0*timelineWidth), right=timelineX+(int)Math.Round(end/1440.0*timelineWidth);
                Rectangle rect=new Rectangle(x,y+2,Math.Max(3,right-x),Math.Max(5,rowHeight-6)); bool hasConflict=AdvancedBlockRules.HasConflict(blocks,block,block);
                using(GraphicsPath path=Rounded(rect,4))using(Brush b=new SolidBrush(hasConflict?conflict:green))e.Graphics.FillPath(b,path);
            }
        }
        using(Pen border=new Pen(Color.FromArgb(205,218,207)))e.Graphics.DrawRectangle(border,new Rectangle(labelWidth+3,1,Math.Max(1,Width-labelWidth-7),Math.Max(1,rowHeight*7-2)));
    }
}

public sealed class AdvancedTimeline : Control {
    readonly List<AdvancedBlock> blocks;
    readonly Color ink=Color.FromArgb(38,58,53), green=Color.FromArgb(61,128,105), pale=Color.FromArgb(237,243,235), conflict=Color.FromArgb(198,95,76);
    AdvancedBlock selected,dragging;
    int dragOffset,originalDay,originalStart,originalEnd;
    bool dragConflict;
    public event EventHandler SelectionChanged;
    public event EventHandler BlocksChanged;
    public AdvancedBlock Selected { get { return selected; } }
    public AdvancedTimeline(List<AdvancedBlock> blocks) { this.blocks=blocks; DoubleBuffered=true; BackColor=Color.FromArgb(252,251,247); Font=new Font("Segoe UI",8.5f); Cursor=Cursors.Hand; }
    int LabelWidth { get { return 62; } }
    int TimelineX { get { return LabelWidth+6; } }
    int TimelineWidth { get { return Math.Max(100,Width-TimelineX-12); } }
    int HeaderHeight { get { return 25; } }
    int RowHeight { get { return Math.Max(34,(Height-HeaderHeight-4)/7); } }
    Rectangle BlockRectangle(AdvancedBlock block) {
        int row=AdvancedBlockRules.DayToRow(block.Day), start=AdvancedBlockRules.Minutes(block.Start), end=AdvancedBlockRules.Minutes(block.End);
        int x=TimelineX+(int)Math.Round(start/1440.0*TimelineWidth), right=TimelineX+(int)Math.Round(end/1440.0*TimelineWidth);
        return new Rectangle(x,HeaderHeight+row*RowHeight+5,Math.Max(5,right-x),Math.Max(12,RowHeight-10));
    }
    GraphicsPath Rounded(Rectangle rect,int radius) {
        GraphicsPath path=new GraphicsPath(); int d=Math.Min(radius*2,Math.Min(rect.Width,rect.Height));
        path.AddArc(rect.X,rect.Y,d,d,180,90); path.AddArc(rect.Right-d,rect.Y,d,d,270,90); path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90); path.AddArc(rect.X,rect.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        for(int hour=0;hour<=24;hour+=3) {
            int x=TimelineX+(int)Math.Round(hour/24.0*TimelineWidth);
            if(hour<24)TextRenderer.DrawText(e.Graphics,hour==0?"12a":hour<12?hour+"a":hour==12?"12p":(hour-12)+"p",Font,new Point(x-9,3),Color.FromArgb(91,108,100));
            using(Pen grid=new Pen(Color.FromArgb(220,228,219)))e.Graphics.DrawLine(grid,x,HeaderHeight,x,HeaderHeight+RowHeight*7);
        }
        for(int row=0;row<7;row++) {
            int y=HeaderHeight+row*RowHeight; Rectangle lane=new Rectangle(TimelineX,y,TimelineWidth,RowHeight-1);
            using(Brush b=new SolidBrush(row%2==0?pale:Color.FromArgb(247,247,242)))e.Graphics.FillRectangle(b,lane);
            TextRenderer.DrawText(e.Graphics,new[]{"Monday","Tuesday","Wednesday","Thursday","Friday","Saturday","Sunday"}[row],Font,new Rectangle(0,y,LabelWidth,RowHeight),ink,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
        }
        foreach(AdvancedBlock block in blocks) {
            Rectangle rect=BlockRectangle(block); bool invalid=object.ReferenceEquals(block,dragging)&&dragConflict;
            using(GraphicsPath path=Rounded(rect,7)) {
                using(Brush b=new SolidBrush(invalid?conflict:(object.ReferenceEquals(block,selected)?Color.FromArgb(47,112,91):green)))e.Graphics.FillPath(b,path);
                if(object.ReferenceEquals(block,selected))using(Pen outline=new Pen(Color.FromArgb(29,72,60),2))e.Graphics.DrawPath(outline,path);
            }
            if(rect.Width>32) {
                string text=block.Activity; if(rect.Width>120)text+="  "+DisplayTime(block.Start)+"–"+DisplayTime(block.End);
                using(Font blockFont=new Font("Segoe UI Semibold",8))TextRenderer.DrawText(e.Graphics,text,blockFont,new Rectangle(rect.X+6,rect.Y,Math.Max(1,rect.Width-12),rect.Height),Color.White,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
            }
        }
        using(Pen border=new Pen(Color.FromArgb(199,214,201)))e.Graphics.DrawRectangle(border,TimelineX,HeaderHeight,TimelineWidth,RowHeight*7);
        if(blocks.Count==0)using(Font emptyFont=new Font("Segoe UI",11))TextRenderer.DrawText(e.Graphics,"No blocks yet — use Add block below",emptyFont,new Rectangle(TimelineX,HeaderHeight,TimelineWidth,RowHeight*7),Color.FromArgb(91,108,100),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    }
    static string DisplayTime(string value) { int minutes=AdvancedBlockRules.Minutes(value); if(minutes<0)return value; return DateTime.Today.AddMinutes(minutes).ToString("h:mm tt"); }
    AdvancedBlock Hit(Point point) { for(int i=blocks.Count-1;i>=0;i--)if(BlockRectangle(blocks[i]).Contains(point))return blocks[i]; return null; }
    int PointerMinutes(int x) { return (int)Math.Round(Math.Max(0,Math.Min(TimelineWidth,x-TimelineX))/(double)TimelineWidth*1440); }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e); if(e.Button!=MouseButtons.Left)return;
        AdvancedBlock hit=Hit(e.Location); selected=hit; if(SelectionChanged!=null)SelectionChanged(this,EventArgs.Empty); Invalidate();
        if(hit==null)return;
        dragging=hit; originalDay=hit.Day; originalStart=AdvancedBlockRules.Minutes(hit.Start); originalEnd=AdvancedBlockRules.Minutes(hit.End); dragOffset=PointerMinutes(e.X)-originalStart; Capture=true;
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e); if(dragging==null || (e.Button&MouseButtons.Left)==0)return;
        int duration=originalEnd-originalStart, start=(int)Math.Round((PointerMinutes(e.X)-dragOffset)/15.0)*15;
        start=Math.Max(0,Math.Min(1440-duration,start)); int row=Math.Max(0,Math.Min(6,(e.Y-HeaderHeight)/Math.Max(1,RowHeight)));
        dragging.Day=AdvancedBlockRules.RowToDay(row); dragging.Start=AdvancedBlockRules.Time(start); dragging.End=AdvancedBlockRules.Time(start+duration);
        dragConflict=AdvancedBlockRules.HasConflict(blocks,dragging,dragging); if(BlocksChanged!=null)BlocksChanged(this,EventArgs.Empty); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e); if(dragging==null)return; Capture=false;
        if(dragConflict) { dragging.Day=originalDay; dragging.Start=AdvancedBlockRules.Time(originalStart); dragging.End=AdvancedBlockRules.Time(originalEnd); MessageBox.Show(this,"That position overlaps another block. Pace put it back where it was.","Blocks cannot overlap"); }
        dragging=null; dragConflict=false; if(BlocksChanged!=null)BlocksChanged(this,EventArgs.Empty); Invalidate();
    }
    public void SelectBlock(AdvancedBlock block) { selected=block; if(SelectionChanged!=null)SelectionChanged(this,EventArgs.Empty); Invalidate(); }
}

public sealed class AdvancedPlanEditorForm : Form {
    readonly string weekStart;
    readonly List<AdvancedBlock> blocks=new List<AdvancedBlock>();
    readonly AdvancedTimeline timeline;
    readonly ComboBox day;
    readonly DateTimePicker start,end;
    readonly TextBox activity;
    readonly Label status;
    readonly Button addOrUpdate,delete;
    readonly Color ink=Color.FromArgb(38,58,53), green=Color.FromArgb(61,128,105), cream=Color.FromArgb(247,244,235), sage=Color.FromArgb(224,235,225);
    bool loadingSelection;
    public List<AdvancedBlock> ResultBlocks { get { List<AdvancedBlock> result=new List<AdvancedBlock>(); foreach(AdvancedBlock block in blocks)result.Add(AdvancedBlockRules.Copy(block,weekStart)); return result; } }
    public AdvancedPlanEditorForm(DateTime monday,IEnumerable<AdvancedBlock> source) {
        weekStart=monday.ToString("yyyy-MM-dd"); foreach(AdvancedBlock block in source)blocks.Add(AdvancedBlockRules.Copy(block,weekStart));
        Text="Advanced plan · "+monday.ToString("MMM d")+"–"+monday.AddDays(6).ToString("MMM d"); ClientSize=new Size(1040,690); StartPosition=FormStartPosition.CenterParent; FormBorderStyle=FormBorderStyle.FixedDialog; BackColor=cream; ForeColor=ink; Font=new Font("Segoe UI",10); MaximizeBox=false;
        Label title=LabelAt(this,"Shape your week",30,22,650,42,24); title.Font=new Font("Segoe UI Semibold",24); title.BackColor=Color.Transparent;
        Label help=LabelAt(this,"Add focused blocks, then drag them across the timeline in 15-minute steps.",32,67,850,28,10); help.ForeColor=Color.FromArgb(91,108,100); help.BackColor=Color.Transparent;
        timeline=new AdvancedTimeline(blocks) { Location=new Point(30,105),Size=new Size(980,365),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right }; Controls.Add(timeline);
        Panel editor=new Panel { Location=new Point(30,486),Size=new Size(980,125),BackColor=sage,Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom }; Controls.Add(editor); Round(editor,16);
        LabelAt(editor,"DAY",18,12,80,20,8); day=new ComboBox { Location=new Point(18,36),Size=new Size(125,29),DropDownStyle=ComboBoxStyle.DropDownList }; day.Items.AddRange(new object[]{"Monday","Tuesday","Wednesday","Thursday","Friday","Saturday","Sunday"}); day.SelectedIndex=0; editor.Controls.Add(day); Round(day,7);
        LabelAt(editor,"START",158,12,80,20,8); start=TimePicker(158,36); editor.Controls.Add(start); Round(start,7);
        LabelAt(editor,"END",274,12,80,20,8); end=TimePicker(274,36); end.Value=DateTime.Today.AddHours(10); editor.Controls.Add(end); Round(end,7);
        LabelAt(editor,"ACTIVITY",390,12,180,20,8); activity=new TextBox { Location=new Point(390,36),Size=new Size(245,29),MaxLength=80 }; editor.Controls.Add(activity); Round(activity,7);
        addOrUpdate=ButtonAt(editor,"Add block",651,33,130,35,delegate { AddOrUpdate(); });
        delete=ButtonAt(editor,"Delete",792,33,90,35,delegate { DeleteSelected(); }); delete.Visible=false;
        ButtonAt(editor,"New",893,33,70,35,delegate { ClearSelection(); });
        status=LabelAt(editor,"Blocks cannot overlap. Drag a block to move it.",18,82,940,25,9); status.ForeColor=Color.FromArgb(76,100,91);
        Button cancel=ButtonAt(this,"Cancel",760,630,110,38,delegate { DialogResult=DialogResult.Cancel; }); cancel.DialogResult=DialogResult.Cancel; CancelButton=cancel;
        Button usePlan=ButtonAt(this,"Use this plan",885,630,125,38,delegate { if(ValidateAll())DialogResult=DialogResult.OK; }); AcceptButton=usePlan;
        timeline.SelectionChanged+=delegate { LoadSelection(); }; timeline.BlocksChanged+=delegate { LoadSelection(); status.Text=AdvancedBlockRules.HasConflict(blocks,timeline.Selected,timeline.Selected)?"Move this block away from the overlapping block.":"Block moved. Save when the week looks right."; status.ForeColor=AdvancedBlockRules.HasConflict(blocks,timeline.Selected,timeline.Selected)?Color.FromArgb(178,73,61):Color.FromArgb(76,100,91); };
        activity.KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.KeyCode==Keys.Enter) { AddOrUpdate(); e.SuppressKeyPress=true; } };
    }
    DateTimePicker TimePicker(int x,int y) { return new DateTimePicker { Location=new Point(x,y),Size=new Size(102,29),Format=DateTimePickerFormat.Custom,CustomFormat="h:mm tt",ShowUpDown=true,Value=DateTime.Today.AddHours(9) }; }
    Label LabelAt(Control parent,string text,int x,int y,int w,int h,float size) { Label label=new Label { Text=text,Location=new Point(x,y),Size=new Size(w,h),Font=new Font("Segoe UI",size),ForeColor=ink }; parent.Controls.Add(label); return label; }
    Button ButtonAt(Control parent,string text,int x,int y,int w,int h,EventHandler handler) { Button button=new Button { Text=text,Location=new Point(x,y),Size=new Size(w,h),FlatStyle=FlatStyle.Flat,BackColor=green,ForeColor=Color.White,Cursor=Cursors.Hand,Font=new Font("Segoe UI Semibold",9) }; button.FlatAppearance.BorderSize=0; button.Click+=handler; parent.Controls.Add(button); Round(button,9); return button; }
    static void Round(Control control,int radius) { Action apply=delegate { if(control.Width<2||control.Height<2)return; GraphicsPath path=new GraphicsPath(); int d=radius*2; path.AddArc(0,0,d,d,180,90); path.AddArc(control.Width-d-1,0,d,d,270,90); path.AddArc(control.Width-d-1,control.Height-d-1,d,d,0,90); path.AddArc(0,control.Height-d-1,d,d,90,90); path.CloseFigure(); Region old=control.Region; control.Region=new Region(path); if(old!=null)old.Dispose(); path.Dispose(); }; control.Resize+=delegate { apply(); }; if(control.IsHandleCreated)apply(); else control.HandleCreated+=delegate { apply(); }; }
    void LoadSelection() {
        AdvancedBlock selected=timeline.Selected; if(selected==null) { delete.Visible=false; addOrUpdate.Text="Add block"; return; }
        loadingSelection=true; day.SelectedIndex=AdvancedBlockRules.DayToRow(selected.Day); start.Value=DateTime.Today.AddMinutes(AdvancedBlockRules.Minutes(selected.Start)); end.Value=DateTime.Today.AddMinutes(AdvancedBlockRules.Minutes(selected.End)); activity.Text=selected.Activity; loadingSelection=false; delete.Visible=true; addOrUpdate.Text="Update";
    }
    void ClearSelection() { timeline.SelectBlock(null); activity.Clear(); day.SelectedIndex=0; start.Value=DateTime.Today.AddHours(9); end.Value=DateTime.Today.AddHours(10); status.Text="Choose a day, start, end, and activity."; status.ForeColor=Color.FromArgb(76,100,91); activity.Focus(); }
    void AddOrUpdate() {
        if(loadingSelection)return; int startMinutes=(int)start.Value.TimeOfDay.TotalMinutes, endMinutes=(int)Math.Min(1440,(end.Value-DateTime.Today).TotalMinutes); if(endMinutes==0)endMinutes=1440;
        if(string.IsNullOrWhiteSpace(activity.Text)) { ShowProblem("Name what this block is for."); return; }
        if(endMinutes<=startMinutes) { ShowProblem("The end must be later than the start."); return; }
        AdvancedBlock selected=timeline.Selected; AdvancedBlock candidate=new AdvancedBlock { WeekStart=weekStart,Day=AdvancedBlockRules.RowToDay(day.SelectedIndex),Start=AdvancedBlockRules.Time(startMinutes),End=AdvancedBlockRules.Time(endMinutes),Activity=activity.Text.Trim() };
        if(AdvancedBlockRules.HasConflict(blocks,candidate,selected)) { ShowProblem("That time overlaps another block on "+AdvancedBlockRules.DayName(candidate.Day)+"."); return; }
        if(selected==null) { blocks.Add(candidate); timeline.SelectBlock(candidate); status.Text="Block added. You can drag it on the timeline."; }
        else { selected.Day=candidate.Day; selected.Start=candidate.Start; selected.End=candidate.End; selected.Activity=candidate.Activity; timeline.Invalidate(); status.Text="Block updated."; }
        status.ForeColor=Color.FromArgb(76,100,91);
    }
    void DeleteSelected() { AdvancedBlock selected=timeline.Selected; if(selected==null)return; blocks.Remove(selected); ClearSelection(); timeline.Invalidate(); status.Text="Block removed."; }
    void ShowProblem(string message) { status.Text=message; status.ForeColor=Color.FromArgb(178,73,61); System.Media.SystemSounds.Exclamation.Play(); }
    bool ValidateAll() { for(int i=0;i<blocks.Count;i++)for(int j=i+1;j<blocks.Count;j++)if(AdvancedBlockRules.Overlaps(blocks[i],blocks[j])) { ShowProblem("Two blocks overlap. Move or edit them before saving."); return false; } return true; }
}

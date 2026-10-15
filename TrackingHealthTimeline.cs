using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

public sealed class TrackingHealthTimeline : Control {
    sealed class Slice {
        public DateTime Start,End;
        public string Kind="";
    }

    readonly List<Slice> slices=new List<Slice>();
    readonly Color tracked=Color.FromArgb(81,145,118);
    readonly Color locked=Color.FromArgb(213,170,92);
    readonly Color manualPaused=Color.FromArgb(104,139,178);
    readonly Color breakColor=Color.FromArgb(108,155,180);
    readonly Color offscreen=Color.FromArgb(139,119,174);
    readonly Color gap=Color.FromArgb(194,105,91);
    Color Empty { get { return PaceTheme.Dark?Color.FromArgb(54,65,59):Color.FromArgb(229,232,226); } }
    DateTime day=DateTime.Today,now=DateTime.Now;
    DateTime fullStart=DateTime.Today,fullEnd=DateTime.Now,viewStart=DateTime.Today,viewEnd=DateTime.Now;
    bool panning;
    int panStartX;
    DateTime panStartViewStart,panStartViewEnd;
    public event EventHandler ViewChanged;
    public int ZoomLevel { get; private set; }
    public DateTime RangeStart { get { return fullStart; } }
    public DateTime RangeEnd { get { return fullEnd; } }
    public DateTime VisibleStart { get { return viewStart; } }
    public DateTime VisibleEnd { get { return viewEnd; } }
    public double TrackedSeconds { get; private set; }
    public double PausedSeconds { get; private set; }
    public double ManualPausedSeconds { get; private set; }
    public double BreakSeconds { get; private set; }
    public double OffscreenSeconds { get; private set; }
    public double GapSeconds { get; private set; }

    public TrackingHealthTimeline() {
        ZoomLevel=1;
        DoubleBuffered=true;
        BackColor=PaceTheme.Surface;
        Font=new Font("Segoe UI",8);
        AccessibleName="Today's tracking timeline";
        AccessibleRole=AccessibleRole.Graphic;
        Cursor=Cursors.Hand;
    }
    public void SetDarkMode(bool dark) { BackColor=PaceTheme.Surface; Invalidate(); }

    static bool Parse(string value,out DateTime result) { return DateTime.TryParse(value,out result); }
    static bool PointEvent(TrackingEventRecord item) { return item!=null && item.Kind!="Gap"; }
    static void ApplyEvent(TrackingEventRecord item,ref bool running,ref bool systemPaused,ref bool userPaused,ref bool onBreak,ref bool inOffscreen) {
        if(item.Kind=="Started") { running=true; systemPaused=false; userPaused=false; }
        else if(item.Kind=="Stopped") { running=false; systemPaused=false; userPaused=false; }
        else if(item.Kind=="Locked" || item.Kind=="Sleep") { if(running)systemPaused=true; }
        else if(item.Kind=="Unlocked" || item.Kind=="Wake") { if(running)systemPaused=false; }
        else if(item.Kind=="Paused") { if(running)userPaused=true; }
        else if(item.Kind=="Resumed") { if(running)userPaused=false; }
        else if(item.Kind=="BreakStarted") { onBreak=true; inOffscreen=false; }
        else if(item.Kind=="BreakEnded")onBreak=false;
        else if(item.Kind=="OffscreenStarted") { inOffscreen=true; onBreak=false; }
        else if(item.Kind=="OffscreenEnded")inOffscreen=false;
    }
    static string SliceKind(bool systemPaused,bool userPaused,bool onBreak,bool inOffscreen) { return inOffscreen?"Offscreen":onBreak?"Break":userPaused?"ManualPaused":systemPaused?"Paused":"Tracked"; }
    void AddSlice(DateTime start,DateTime end,string kind) {
        if(end<=start)return;
        slices.Add(new Slice { Start=start,End=end,Kind=kind });
        double seconds=(end-start).TotalSeconds;
        if(kind=="Tracked")TrackedSeconds+=seconds;
        else if(kind=="Paused")PausedSeconds+=seconds;
        else if(kind=="ManualPaused")ManualPausedSeconds+=seconds;
        else if(kind=="Break")BreakSeconds+=seconds;
        else if(kind=="Offscreen")OffscreenSeconds+=seconds;
        else if(kind=="Gap")GapSeconds+=seconds;
    }
    static string Duration(double seconds) {
        int minutes=Math.Max(0,(int)Math.Round(seconds/60));
        int hours=minutes/60,remainder=minutes%60;
        if(hours>0 && remainder>0)return hours+"h "+remainder+"m";
        if(hours>0)return hours+"h";
        return minutes+"m";
    }

    public void SetData(IEnumerable<TrackingEventRecord> records,DateTime current,bool trackingOpen,bool currentlySystemPaused,bool currentlyUserPaused) {
        bool sameDay=day==current.Date; DateTime oldFullStart=fullStart,oldFullEnd=fullEnd,oldViewStart=viewStart,oldViewEnd=viewEnd;
        now=current; day=current.Date; DateTime endOfDay=day.AddDays(1),visibleEnd=current<endOfDay?current:endOfDay;
        slices.Clear(); TrackedSeconds=0; PausedSeconds=0; ManualPausedSeconds=0; BreakSeconds=0; OffscreenSeconds=0; GapSeconds=0;
        List<KeyValuePair<DateTime,TrackingEventRecord>> points=new List<KeyValuePair<DateTime,TrackingEventRecord>>();
        List<Slice> gaps=new List<Slice>();
        if(records!=null)foreach(TrackingEventRecord item in records) {
            DateTime start,eventEnd;
            if(item==null || !Parse(item.Start,out start))continue;
            if(item.Kind=="Gap" && Parse(item.End,out eventEnd)) {
                DateTime clippedStart=start>day?start:day,clippedEnd=eventEnd<visibleEnd?eventEnd:visibleEnd;
                if(clippedEnd>clippedStart)gaps.Add(new Slice { Start=clippedStart,End=clippedEnd,Kind="Gap" });
            } else if(PointEvent(item) && start<=visibleEnd)points.Add(new KeyValuePair<DateTime,TrackingEventRecord>(start,item));
        }
        points.Sort(delegate(KeyValuePair<DateTime,TrackingEventRecord> a,KeyValuePair<DateTime,TrackingEventRecord> b) { return a.Key.CompareTo(b.Key); });
        bool running=false,systemPaused=false,userPaused=false,onBreak=false,inOffscreen=false,hasState=false;
        foreach(KeyValuePair<DateTime,TrackingEventRecord> point in points)if(point.Key<day) { ApplyEvent(point.Value,ref running,ref systemPaused,ref userPaused,ref onBreak,ref inOffscreen); hasState=true; }
        DateTime cursor=day;
        foreach(KeyValuePair<DateTime,TrackingEventRecord> point in points) {
            if(point.Key<day)continue;
            DateTime at=point.Key>visibleEnd?visibleEnd:point.Key;
            if(hasState && running)AddSlice(cursor,at,SliceKind(systemPaused,userPaused,onBreak,inOffscreen));
            cursor=at; ApplyEvent(point.Value,ref running,ref systemPaused,ref userPaused,ref onBreak,ref inOffscreen); hasState=true;
            if(point.Key>=visibleEnd)break;
        }
        if(hasState && running)AddSlice(cursor,visibleEnd,SliceKind(systemPaused,userPaused,onBreak,inOffscreen));
        else if(!hasState && trackingOpen)AddSlice(day,visibleEnd,currentlyUserPaused?"ManualPaused":currentlySystemPaused?"Paused":"Tracked");

        gaps.Sort(delegate(Slice a,Slice b) { return a.Start.CompareTo(b.Start); });
        DateTime gapStart=DateTime.MinValue,gapEnd=DateTime.MinValue;
        foreach(Slice item in gaps) {
            if(gapStart==DateTime.MinValue) { gapStart=item.Start; gapEnd=item.End; }
            else if(item.Start<=gapEnd) { if(item.End>gapEnd)gapEnd=item.End; }
            else { AddSlice(gapStart,gapEnd,"Gap"); gapStart=item.Start; gapEnd=item.End; }
        }
        if(gapStart!=DateTime.MinValue)AddSlice(gapStart,gapEnd,"Gap");
        DateTime first=DateTime.MaxValue;
        foreach(KeyValuePair<DateTime,TrackingEventRecord> point in points)if(point.Key>=day && point.Key<visibleEnd && point.Key<first)first=point.Key;
        if(first==DateTime.MaxValue)foreach(Slice item in gaps)if(item.Start>=day && item.Start<visibleEnd && item.Start<first)first=item.Start;
        if(first==DateTime.MaxValue)foreach(Slice item in slices)if(item.Start<first)first=item.Start;
        fullEnd=visibleEnd; fullStart=first==DateTime.MaxValue?day:first;
        if(fullEnd<=fullStart)fullStart=fullEnd.AddMinutes(-1)>day?fullEnd.AddMinutes(-1):day;
        if(!sameDay)ZoomLevel=1;
        if(ZoomLevel<=1 || oldFullEnd<=oldFullStart) { ZoomLevel=1; viewStart=fullStart; viewEnd=fullEnd; }
        else {
            double oldCenter=((oldViewStart-oldFullStart).TotalSeconds+(oldViewEnd-oldFullStart).TotalSeconds)/2.0/Math.Max(1,(oldFullEnd-oldFullStart).TotalSeconds);
            SetView(ZoomLevel,Math.Max(0,Math.Min(1,oldCenter)),false);
        }
        string dayName=day==DateTime.Today?"Today":day.ToString("ddd, MMM d");
        AccessibleName=dayName+" tracking timeline";
        AccessibleDescription=dayName+": "+Duration(TrackedSeconds)+" tracked, "+Duration(BreakSeconds)+" break, "+Duration(OffscreenSeconds)+" offscreen activity, "+Duration(ManualPausedSeconds)+" manually paused, "+Duration(PausedSeconds)+" locked or asleep, "+Duration(GapSeconds)+" not tracked. Timeline begins at "+fullStart.ToString("h:mm tt")+". Zoom "+ZoomLevel+" times.";
        Invalidate();
    }

    int TimeX(DateTime value,Rectangle lane) {
        double fraction=(value-viewStart).TotalSeconds/Math.Max(1,(viewEnd-viewStart).TotalSeconds);
        return lane.Left+(int)Math.Round(Math.Max(0,Math.Min(1,fraction))*lane.Width);
    }
    void SetView(int zoom,double centerFraction,bool notify) {
        ZoomLevel=Math.Max(1,Math.Min(8,zoom)); double total=Math.Max(1,(fullEnd-fullStart).TotalSeconds),span=total/ZoomLevel;
        DateTime center=fullStart.AddSeconds(total*Math.Max(0,Math.Min(1,centerFraction)));
        viewStart=center.AddSeconds(-span/2); viewEnd=viewStart.AddSeconds(span);
        if(viewStart<fullStart) { viewStart=fullStart; viewEnd=viewStart.AddSeconds(span); }
        if(viewEnd>fullEnd) { viewEnd=fullEnd; viewStart=viewEnd.AddSeconds(-span); }
        if(ZoomLevel==1) { viewStart=fullStart; viewEnd=fullEnd; }
        if(notify && ViewChanged!=null)ViewChanged(this,EventArgs.Empty); Invalidate();
    }
    public void ZoomIn() { double center=(viewStart.AddTicks((viewEnd-viewStart).Ticks/2)-fullStart).TotalSeconds/Math.Max(1,(fullEnd-fullStart).TotalSeconds); SetView(ZoomLevel*2,center,true); }
    public void ZoomOut() { double center=(viewStart.AddTicks((viewEnd-viewStart).Ticks/2)-fullStart).TotalSeconds/Math.Max(1,(fullEnd-fullStart).TotalSeconds); SetView(ZoomLevel/2,center,true); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Rectangle lane=new Rectangle(10,25,Math.Max(20,ClientSize.Width-20),18); double fraction=Math.Max(0,Math.Min(1,(e.X-lane.Left)/(double)Math.Max(1,lane.Width))); DateTime anchor=viewStart.AddSeconds((viewEnd-viewStart).TotalSeconds*fraction); int next=e.Delta>0?ZoomLevel*2:ZoomLevel/2; next=Math.Max(1,Math.Min(8,next)); double total=Math.Max(1,(fullEnd-fullStart).TotalSeconds),span=total/next; DateTime start=anchor.AddSeconds(-span*fraction); double center=(start.AddSeconds(span/2)-fullStart).TotalSeconds/total; SetView(next,center,true); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if(e.Button==MouseButtons.Left && ZoomLevel>1) { panning=true; panStartX=e.X; panStartViewStart=viewStart; panStartViewEnd=viewEnd; Capture=true; Cursor=Cursors.SizeWE; } }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if(!panning)return; double shift=-(e.X-panStartX)/(double)Math.Max(1,ClientSize.Width)*(panStartViewEnd-panStartViewStart).TotalSeconds; DateTime start=panStartViewStart.AddSeconds(shift),end=panStartViewEnd.AddSeconds(shift); if(start<fullStart) { start=fullStart; end=start+(panStartViewEnd-panStartViewStart); } if(end>fullEnd) { end=fullEnd; start=end-(panStartViewEnd-panStartViewStart); } viewStart=start; viewEnd=end; if(ViewChanged!=null)ViewChanged(this,EventArgs.Empty); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if(!panning)return; panning=false; Capture=false; Cursor=Cursors.Hand; }
    static GraphicsPath RoundPath(Rectangle rectangle,int radius) {
        GraphicsPath path=new GraphicsPath();
        int diameter=Math.Min(radius*2,Math.Min(rectangle.Width,rectangle.Height));
        path.AddArc(rectangle.Left,rectangle.Top,diameter,diameter,180,90);
        path.AddArc(rectangle.Right-diameter,rectangle.Top,diameter,diameter,270,90);
        path.AddArc(rectangle.Right-diameter,rectangle.Bottom-diameter,diameter,diameter,0,90);
        path.AddArc(rectangle.Left,rectangle.Bottom-diameter,diameter,diameter,90,90);
        path.CloseFigure(); return path;
    }
    static void FillRound(Graphics graphics,Brush brush,Rectangle rectangle,int radius) {
        if(rectangle.Width<=0 || rectangle.Height<=0)return;
        using(GraphicsPath path=RoundPath(rectangle,radius))graphics.FillPath(brush,path);
    }
    void DrawLegend(Graphics graphics,string label,Color color,int x,int y) {
        using(Brush brush=new SolidBrush(color))graphics.FillEllipse(brush,x,y+3,8,8);
        TextRenderer.DrawText(graphics,label,Font,new Point(x+12,y),PaceTheme.Muted,TextFormatFlags.NoPadding);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color ink=PaceTheme.Text,muted=PaceTheme.Muted;
        using(Font heading=new Font("Segoe UI Semibold",9))TextRenderer.DrawText(e.Graphics,day==DateTime.Today?"TODAY'S TRACKING":day.ToString("ddd, MMM d").ToUpperInvariant(),heading,new Point(10,3),ink,TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics,Duration(TrackedSeconds)+" tracked",Font,new Rectangle(150,3,ClientSize.Width-160,18),muted,TextFormatFlags.Right|TextFormatFlags.NoPadding);
        Rectangle lane=new Rectangle(10,25,Math.Max(20,ClientSize.Width-20),18);
        using(Brush brush=new SolidBrush(Empty))FillRound(e.Graphics,brush,lane,8);
        GraphicsState laneState=e.Graphics.Save(); using(GraphicsPath lanePath=RoundPath(lane,8))e.Graphics.SetClip(lanePath);
        foreach(Slice slice in slices)if(slice.Kind!="Gap") {
            int left=TimeX(slice.Start,lane),right=TimeX(slice.End,lane);
            Rectangle block=new Rectangle(left,lane.Top,Math.Max(2,right-left),lane.Height);
            Color color=slice.Kind=="Offscreen"?offscreen:slice.Kind=="Break"?breakColor:slice.Kind=="ManualPaused"?manualPaused:slice.Kind=="Paused"?locked:tracked;
            using(Brush brush=new SolidBrush(color))e.Graphics.FillRectangle(brush,block);
        }
        foreach(Slice slice in slices)if(slice.Kind=="Gap") {
            int left=TimeX(slice.Start,lane),right=TimeX(slice.End,lane);
            Rectangle block=new Rectangle(left,lane.Top,Math.Max(2,right-left),lane.Height);
            using(Brush brush=new SolidBrush(gap))e.Graphics.FillRectangle(brush,block);
        }
        e.Graphics.Restore(laneState);
        if(now>=viewStart && now<=viewEnd && day==DateTime.Today) {
            int currentX=TimeX(now,lane); using(Pen pen=new Pen(ink,2))e.Graphics.DrawLine(pen,currentX,lane.Top-3,currentX,lane.Bottom+3);
        }
        for(int i=0;i<5;i++) {
            DateTime labelTime=viewStart.AddTicks((long)((viewEnd-viewStart).Ticks*i/4.0)); string label=labelTime.ToString("h:mmtt").ToLowerInvariant();
            int x=lane.Left+(int)Math.Round(lane.Width*i/4.0),width=52;
            if(i==0)x=lane.Left; else if(i==4)x=lane.Right-width; else x-=width/2;
            TextRenderer.DrawText(e.Graphics,label,Font,new Rectangle(x,47,width,15),muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPadding);
        }
        int legendY=Math.Max(63,ClientSize.Height-18);
        DrawLegend(e.Graphics,"Tracked",tracked,10,legendY);
        DrawLegend(e.Graphics,"Paused",manualPaused,88,legendY);
        DrawLegend(e.Graphics,"Break",breakColor,160,legendY);
        DrawLegend(e.Graphics,"Offscreen activity",offscreen,222,legendY);
        DrawLegend(e.Graphics,"Locked / sleep",locked,350,legendY);
        DrawLegend(e.Graphics,"Not tracked",gap,462,legendY);
        DrawLegend(e.Graphics,"No timeline data",Empty,565,legendY);
    }
}

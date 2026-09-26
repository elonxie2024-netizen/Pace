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
    readonly Color paused=Color.FromArgb(213,170,92);
    readonly Color gap=Color.FromArgb(194,105,91);
    readonly Color empty=Color.FromArgb(229,232,226);
    DateTime day=DateTime.Today,now=DateTime.Now;
    public double TrackedSeconds { get; private set; }
    public double PausedSeconds { get; private set; }
    public double GapSeconds { get; private set; }

    public TrackingHealthTimeline() {
        DoubleBuffered=true;
        BackColor=Color.FromArgb(252,251,247);
        Font=new Font("Segoe UI",8);
        AccessibleName="Today's tracking timeline";
        AccessibleRole=AccessibleRole.Graphic;
    }

    static bool Parse(string value,out DateTime result) { return DateTime.TryParse(value,out result); }
    static bool PointEvent(TrackingEventRecord item) { return item!=null && item.Kind!="Gap"; }
    static void ApplyEvent(TrackingEventRecord item,ref bool running,ref bool isPaused) {
        if(item.Kind=="Started") { running=true; isPaused=false; }
        else if(item.Kind=="Stopped") { running=false; isPaused=false; }
        else if(item.Kind=="Locked" || item.Kind=="Sleep") { if(running)isPaused=true; }
        else if(item.Kind=="Unlocked" || item.Kind=="Wake") { if(running)isPaused=false; }
    }
    void AddSlice(DateTime start,DateTime end,string kind) {
        if(end<=start)return;
        slices.Add(new Slice { Start=start,End=end,Kind=kind });
        double seconds=(end-start).TotalSeconds;
        if(kind=="Tracked")TrackedSeconds+=seconds;
        else if(kind=="Paused")PausedSeconds+=seconds;
        else if(kind=="Gap")GapSeconds+=seconds;
    }
    static string Duration(double seconds) {
        int minutes=Math.Max(0,(int)Math.Round(seconds/60));
        int hours=minutes/60,remainder=minutes%60;
        if(hours>0 && remainder>0)return hours+"h "+remainder+"m";
        if(hours>0)return hours+"h";
        return minutes+"m";
    }

    public void SetData(IEnumerable<TrackingEventRecord> records,DateTime current,bool trackingOpen,bool currentlyPaused) {
        now=current; day=current.Date; DateTime endOfDay=day.AddDays(1),visibleEnd=current<endOfDay?current:endOfDay;
        slices.Clear(); TrackedSeconds=0; PausedSeconds=0; GapSeconds=0;
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
        bool running=false,isPaused=false,hasState=false;
        foreach(KeyValuePair<DateTime,TrackingEventRecord> point in points)if(point.Key<day) { ApplyEvent(point.Value,ref running,ref isPaused); hasState=true; }
        DateTime cursor=day;
        foreach(KeyValuePair<DateTime,TrackingEventRecord> point in points) {
            if(point.Key<day)continue;
            DateTime at=point.Key>visibleEnd?visibleEnd:point.Key;
            if(hasState && running)AddSlice(cursor,at,isPaused?"Paused":"Tracked");
            cursor=at; ApplyEvent(point.Value,ref running,ref isPaused); hasState=true;
            if(point.Key>=visibleEnd)break;
        }
        if(hasState && running)AddSlice(cursor,visibleEnd,isPaused?"Paused":"Tracked");
        else if(!hasState && trackingOpen)AddSlice(day,visibleEnd,currentlyPaused?"Paused":"Tracked");

        gaps.Sort(delegate(Slice a,Slice b) { return a.Start.CompareTo(b.Start); });
        DateTime gapStart=DateTime.MinValue,gapEnd=DateTime.MinValue;
        foreach(Slice item in gaps) {
            if(gapStart==DateTime.MinValue) { gapStart=item.Start; gapEnd=item.End; }
            else if(item.Start<=gapEnd) { if(item.End>gapEnd)gapEnd=item.End; }
            else { AddSlice(gapStart,gapEnd,"Gap"); gapStart=item.Start; gapEnd=item.End; }
        }
        if(gapStart!=DateTime.MinValue)AddSlice(gapStart,gapEnd,"Gap");
        AccessibleDescription="Today: "+Duration(TrackedSeconds)+" tracked, "+Duration(PausedSeconds)+" locked or asleep, "+Duration(GapSeconds)+" not tracked.";
        Invalidate();
    }

    int TimeX(DateTime value,Rectangle lane) {
        double fraction=(value-day).TotalSeconds/TimeSpan.FromDays(1).TotalSeconds;
        return lane.Left+(int)Math.Round(Math.Max(0,Math.Min(1,fraction))*lane.Width);
    }
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
        TextRenderer.DrawText(graphics,label,Font,new Point(x+12,y),Color.FromArgb(75,94,86),TextFormatFlags.NoPadding);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color ink=Color.FromArgb(38,58,53),muted=Color.FromArgb(91,108,100);
        using(Font heading=new Font("Segoe UI Semibold",9))TextRenderer.DrawText(e.Graphics,"TODAY'S TRACKING",heading,new Point(10,3),ink,TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics,Duration(TrackedSeconds)+" tracked",Font,new Rectangle(150,3,ClientSize.Width-160,18),muted,TextFormatFlags.Right|TextFormatFlags.NoPadding);
        Rectangle lane=new Rectangle(10,25,Math.Max(20,ClientSize.Width-20),18);
        using(Brush brush=new SolidBrush(empty))FillRound(e.Graphics,brush,lane,8);
        GraphicsState laneState=e.Graphics.Save(); using(GraphicsPath lanePath=RoundPath(lane,8))e.Graphics.SetClip(lanePath);
        foreach(Slice slice in slices)if(slice.Kind!="Gap") {
            int left=TimeX(slice.Start,lane),right=TimeX(slice.End,lane);
            Rectangle block=new Rectangle(left,lane.Top,Math.Max(2,right-left),lane.Height);
            using(Brush brush=new SolidBrush(slice.Kind=="Paused"?paused:tracked))e.Graphics.FillRectangle(brush,block);
        }
        foreach(Slice slice in slices)if(slice.Kind=="Gap") {
            int left=TimeX(slice.Start,lane),right=TimeX(slice.End,lane);
            Rectangle block=new Rectangle(left,lane.Top,Math.Max(2,right-left),lane.Height);
            using(Brush brush=new SolidBrush(gap))e.Graphics.FillRectangle(brush,block);
        }
        e.Graphics.Restore(laneState);
        if(now>=day && now<day.AddDays(1)) {
            int currentX=TimeX(now,lane); using(Pen pen=new Pen(ink,2))e.Graphics.DrawLine(pen,currentX,lane.Top-3,currentX,lane.Bottom+3);
        }
        string[] labels={"12a","6a","12p","6p","12a"};
        for(int i=0;i<labels.Length;i++) {
            int x=lane.Left+(int)Math.Round(lane.Width*i/4.0),width=32;
            if(i==0)x=lane.Left; else if(i==labels.Length-1)x=lane.Right-width; else x-=width/2;
            TextRenderer.DrawText(e.Graphics,labels[i],Font,new Rectangle(x,47,width,15),muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPadding);
        }
        int legendY=Math.Max(63,ClientSize.Height-18);
        DrawLegend(e.Graphics,"Tracked",tracked,10,legendY);
        DrawLegend(e.Graphics,"Locked / sleep",paused,91,legendY);
        DrawLegend(e.Graphics,"Not tracked",gap,211,legendY);
        DrawLegend(e.Graphics,"No timeline data",empty,310,legendY);
    }
}

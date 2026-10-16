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
    sealed class Marker {
        public DateTime At;
        public string Kind="";
    }

    readonly List<Slice> slices=new List<Slice>();
    readonly List<Marker> markers=new List<Marker>();
    readonly ToolTip hoverTip=new ToolTip { InitialDelay=200,ReshowDelay=100,AutoPopDelay=6000,ShowAlways=true };
    Slice hoveredSlice;
    Marker hoveredMarker;
    readonly Color tracked=Color.FromArgb(81,145,118);
    readonly Color locked=Color.FromArgb(213,170,92);
    readonly Color manualPaused=Color.FromArgb(104,139,178);
    readonly Color breakColor=Color.FromArgb(108,155,180);
    readonly Color offscreen=Color.FromArgb(139,119,174);
    readonly Color gap=Color.FromArgb(194,105,91);
    readonly Color breakCanceled=Color.FromArgb(190,126,71);
    readonly Color breakEndedEarly=Color.FromArgb(174,85,101);
    Color Empty { get { return PaceTheme.Dark?Color.FromArgb(54,65,59):Color.FromArgb(229,232,226); } }
    DateTime day=DateTime.Today,now=DateTime.Now;
    DateTime fullStart=DateTime.Today,fullEnd=DateTime.Now,viewStart=DateTime.Today,viewEnd=DateTime.Now;
    bool panning;
    int panStartX,panStartY,panStartTop,baseWidth,baseHeight;
    double verticalFraction;
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
    public int BreakCanceledMarkers { get; private set; }
    public int BreakEndedEarlyMarkers { get; private set; }

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
        else if(item.Kind=="BreakEnded" || item.Kind=="BreakEndedEarly")onBreak=false;
        else if(item.Kind=="OffscreenStarted") { inOffscreen=true; onBreak=false; }
        else if(item.Kind=="OffscreenEnded")inOffscreen=false;
    }
    static string SliceKind(bool systemPaused,bool userPaused,bool onBreak,bool inOffscreen) { return inOffscreen?"Offscreen":onBreak?"Break":userPaused?"ManualPaused":systemPaused?"Paused":"Tracked"; }
    void AddSlice(DateTime start,DateTime end,string kind) {
        if(end<=start)return;
        if(slices.Count>0 && slices[slices.Count-1].Kind==kind && slices[slices.Count-1].End==start)slices[slices.Count-1].End=end;
        else slices.Add(new Slice { Start=start,End=end,Kind=kind });
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

    public void SetData(IEnumerable<TrackingEventRecord> records,DateTime current,bool trackingOpen,bool currentlySystemPaused,bool currentlyUserPaused) { SetData(records,current,trackingOpen,currentlySystemPaused,currentlyUserPaused,null,null); }
    public void SetData(IEnumerable<TrackingEventRecord> records,DateTime current,bool trackingOpen,bool currentlySystemPaused,bool currentlyUserPaused,DateTime? activityStart,DateTime? activityEnd) {
        bool sameDay=day==current.Date; DateTime oldFullStart=fullStart,oldFullEnd=fullEnd,oldViewStart=viewStart,oldViewEnd=viewEnd;
        now=current; day=current.Date; DateTime endOfDay=day.AddDays(1),visibleEnd=current<endOfDay?current:endOfDay;
        slices.Clear(); markers.Clear(); hoveredSlice=null; hoveredMarker=null; hoverTip.Hide(this); TrackedSeconds=0; PausedSeconds=0; ManualPausedSeconds=0; BreakSeconds=0; OffscreenSeconds=0; GapSeconds=0; BreakCanceledMarkers=0; BreakEndedEarlyMarkers=0;
        List<KeyValuePair<DateTime,TrackingEventRecord>> points=new List<KeyValuePair<DateTime,TrackingEventRecord>>();
        List<Slice> gaps=new List<Slice>();
        if(records!=null)foreach(TrackingEventRecord item in records) {
            DateTime start,eventEnd;
            if(item==null || !Parse(item.Start,out start))continue;
            if((item.Kind=="BreakCanceled" || item.Kind=="BreakEndedEarly") && start>=day && start<=visibleEnd) { markers.Add(new Marker { At=start,Kind=item.Kind }); if(item.Kind=="BreakCanceled")BreakCanceledMarkers++; else BreakEndedEarlyMarkers++; }
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
        DateTime requestedStart=activityStart.HasValue && activityStart.Value.Date==day?activityStart.Value:first==DateTime.MaxValue?day:first;
        DateTime requestedEnd=activityEnd.HasValue && activityEnd.Value.Date==day?activityEnd.Value:visibleEnd;
        if(requestedStart<day)requestedStart=day; if(requestedEnd>visibleEnd)requestedEnd=visibleEnd; if(requestedEnd<requestedStart)requestedEnd=requestedStart;
        List<Slice> cropped=new List<Slice>(); foreach(Slice item in slices) { DateTime clippedStart=item.Start>requestedStart?item.Start:requestedStart,clippedEnd=item.End<requestedEnd?item.End:requestedEnd; if(clippedEnd<=clippedStart)continue; if(cropped.Count>0 && cropped[cropped.Count-1].Kind==item.Kind && cropped[cropped.Count-1].End==clippedStart)cropped[cropped.Count-1].End=clippedEnd; else cropped.Add(new Slice { Start=clippedStart,End=clippedEnd,Kind=item.Kind }); }
        slices.Clear(); TrackedSeconds=0; PausedSeconds=0; ManualPausedSeconds=0; BreakSeconds=0; OffscreenSeconds=0; GapSeconds=0; foreach(Slice item in cropped)AddSlice(item.Start,item.End,item.Kind);
        markers.RemoveAll(delegate(Marker marker) { return marker.At<requestedStart || marker.At>requestedEnd; }); BreakCanceledMarkers=markers.FindAll(delegate(Marker marker) { return marker.Kind=="BreakCanceled"; }).Count; BreakEndedEarlyMarkers=markers.Count-BreakCanceledMarkers;
        fullEnd=requestedEnd; fullStart=requestedStart;
        if(fullEnd<=fullStart)fullStart=fullEnd.AddMinutes(-1)>day?fullEnd.AddMinutes(-1):day;
        if(!sameDay)ZoomLevel=1;
        if(ZoomLevel<=1 || oldFullEnd<=oldFullStart) { ZoomLevel=1; viewStart=fullStart; viewEnd=fullEnd; }
        else {
            double oldCenter=((oldViewStart-oldFullStart).TotalSeconds+(oldViewEnd-oldFullStart).TotalSeconds)/2.0/Math.Max(1,(oldFullEnd-oldFullStart).TotalSeconds);
            SetView(ZoomLevel,Math.Max(0,Math.Min(1,oldCenter)),false);
        }
        SyncScale();
        string dayName=day==DateTime.Today?"Today":day.ToString("ddd, MMM d");
        AccessibleName=dayName+" tracking timeline";
        AccessibleDescription=dayName+": "+Duration(TrackedSeconds)+" tracked, "+Duration(BreakSeconds)+" break, "+Duration(OffscreenSeconds)+" offscreen activity, "+Duration(ManualPausedSeconds)+" manually paused, "+Duration(PausedSeconds)+" locked or asleep, "+Duration(GapSeconds)+" not tracked. Timeline begins at "+fullStart.ToString("h:mm tt")+". Zoom "+ZoomLevel+" times.";
        Invalidate();
    }

    int TimeX(DateTime value,Rectangle lane) {
        double fraction=(value-fullStart).TotalSeconds/Math.Max(1,(fullEnd-fullStart).TotalSeconds);
        return lane.Left+(int)Math.Round(Math.Max(0,Math.Min(1,fraction))*lane.Width);
    }
    int ViewportWidth { get { return Parent==null?Math.Max(20,baseWidth>0?baseWidth:Width):Math.Max(20,Parent.ClientSize.Width); } }
    int ViewportHeight { get { return Parent==null?Math.Max(20,baseHeight>0?baseHeight:Height):Math.Max(20,Parent.ClientSize.Height); } }
    void SyncScale() {
        int viewport=ViewportWidth,viewportHeight=ViewportHeight;
        if(baseWidth<=0 || ZoomLevel==1)baseWidth=viewport;
        if(baseHeight<=0 || ZoomLevel==1)baseHeight=Math.Max(105,viewportHeight);
        int desired=Math.Max(20,baseWidth*Math.Max(1,ZoomLevel));
        if(Width!=desired)Width=desired;
        int desiredHeight=Math.Max(20,baseHeight*Math.Max(1,ZoomLevel));
        if(Height!=desiredHeight)Height=desiredHeight;
        double total=Math.Max(1,(fullEnd-fullStart).TotalSeconds),span=Math.Max(1,(viewEnd-viewStart).TotalSeconds);
        double travel=Math.Max(0,total-span),fraction=travel<=0?0:(viewStart-fullStart).TotalSeconds/travel;
        Left=-(int)Math.Round(Math.Max(0,Math.Min(1,fraction))*Math.Max(0,Width-viewport));
        Top=-(int)Math.Round(Math.Max(0,Math.Min(1,verticalFraction))*Math.Max(0,Height-viewportHeight));
    }
    void SetView(int zoom,double centerFraction,bool notify) {
        ZoomLevel=Math.Max(1,Math.Min(8,zoom)); double total=Math.Max(1,(fullEnd-fullStart).TotalSeconds),span=total/ZoomLevel;
        DateTime center=fullStart.AddSeconds(total*Math.Max(0,Math.Min(1,centerFraction)));
        viewStart=center.AddSeconds(-span/2); viewEnd=viewStart.AddSeconds(span);
        if(viewStart<fullStart) { viewStart=fullStart; viewEnd=viewStart.AddSeconds(span); }
        if(viewEnd>fullEnd) { viewEnd=fullEnd; viewStart=viewEnd.AddSeconds(-span); }
        if(ZoomLevel==1) { viewStart=fullStart; viewEnd=fullEnd; }
        SyncScale();
        if(notify && ViewChanged!=null)ViewChanged(this,EventArgs.Empty); Invalidate();
    }
    void FocusVertically(int zoom) { verticalFraction=zoom<=1?0:Math.Max(0,Math.Min(1,(34.0*zoom-ViewportHeight/2.0)/Math.Max(1,baseHeight*zoom-ViewportHeight))); }
    public void ZoomIn() { double center=(viewStart.AddTicks((viewEnd-viewStart).Ticks/2)-fullStart).TotalSeconds/Math.Max(1,(fullEnd-fullStart).TotalSeconds); int next=Math.Min(8,ZoomLevel*2); FocusVertically(next); SetView(next,center,true); }
    public void ZoomOut() { double center=(viewStart.AddTicks((viewEnd-viewStart).Ticks/2)-fullStart).TotalSeconds/Math.Max(1,(fullEnd-fullStart).TotalSeconds); int next=Math.Max(1,ZoomLevel/2); FocusVertically(next); SetView(next,center,true); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); int visibleLeft=Math.Max(0,-Left),viewport=ViewportWidth; double fraction=Math.Max(0,Math.Min(1,(e.X-visibleLeft-10*ZoomLevel)/(double)Math.Max(1,viewport-20*ZoomLevel))); Rectangle lane=new Rectangle(10*ZoomLevel,25*ZoomLevel,Math.Max(20,ClientSize.Width-20*ZoomLevel),18*ZoomLevel); DateTime anchor=fullStart.AddSeconds((fullEnd-fullStart).TotalSeconds*Math.Max(0,Math.Min(1,(e.X-lane.Left)/(double)Math.Max(1,lane.Width)))); int next=e.Delta>0?ZoomLevel*2:ZoomLevel/2; next=Math.Max(1,Math.Min(8,next)); double total=Math.Max(1,(fullEnd-fullStart).TotalSeconds),span=total/next; DateTime start=anchor.AddSeconds(-span*fraction); double center=(start.AddSeconds(span/2)-fullStart).TotalSeconds/total; FocusVertically(next); SetView(next,center,true); }
    Rectangle ScaledLane() { int scale=Math.Max(1,ZoomLevel); return new Rectangle(10*scale,25*scale,Math.Max(20,ClientSize.Width-20*scale),18*scale); }
    static string SliceLabel(string kind) { return kind=="Offscreen"?"Offscreen activity":kind=="ManualPaused"?"Paused":kind=="Paused"?"Locked / sleep":kind=="Gap"?"Not tracked":kind; }
    string HoverText(Slice slice) { return SliceLabel(slice.Kind)+"  ·  "+Duration((slice.End-slice.Start).TotalSeconds); }
    string MarkerText(Marker marker) { return (marker.Kind=="BreakCanceled"?"Break canceled":"Break ended early")+"  ·  "+marker.At.ToString("h:mm tt"); }
    void UpdateHover(Point point) {
        Rectangle lane=ScaledLane(); int scale=Math.Max(1,ZoomLevel); Marker markerHit=null; Slice sliceHit=null;
        if(point.Y>=lane.Bottom && point.Y<=lane.Bottom+12*scale)for(int i=markers.Count-1;i>=0;i--)if(Math.Abs(TimeX(markers[i].At,lane)-point.X)<=5*scale) { markerHit=markers[i]; break; }
        if(markerHit==null && lane.Contains(point)) { double fraction=Math.Max(0,Math.Min(1,(point.X-lane.Left)/(double)Math.Max(1,lane.Width))); DateTime at=fullStart.AddSeconds((fullEnd-fullStart).TotalSeconds*fraction); for(int i=slices.Count-1;i>=0;i--)if(at>=slices[i].Start && at<=slices[i].End) { sliceHit=slices[i]; break; } }
        if(markerHit==hoveredMarker && sliceHit==hoveredSlice)return; hoveredMarker=markerHit; hoveredSlice=sliceHit; string text=markerHit!=null?MarkerText(markerHit):sliceHit!=null?HoverText(sliceHit):""; hoverTip.SetToolTip(this,text); hoverTip.Hide(this); if(text.Length>0 && IsHandleCreated)hoverTip.Show(text,this,point.X+12*scale,point.Y+12*scale,6000);
    }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if(e.Button==MouseButtons.Left && ZoomLevel>1) { hoverTip.Hide(this); Point screen=PointToScreen(e.Location); panning=true; panStartX=screen.X; panStartY=screen.Y; panStartTop=Top; panStartViewStart=viewStart; panStartViewEnd=viewEnd; Capture=true; Cursor=Cursors.SizeAll; } }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if(!panning) { UpdateHover(e.Location); return; } Point screen=PointToScreen(e.Location); double shift=-(screen.X-panStartX)/(double)Math.Max(1,ViewportWidth)*(panStartViewEnd-panStartViewStart).TotalSeconds; DateTime start=panStartViewStart.AddSeconds(shift),end=panStartViewEnd.AddSeconds(shift); if(start<fullStart) { start=fullStart; end=start+(panStartViewEnd-panStartViewStart); } if(end>fullEnd) { end=fullEnd; start=end-(panStartViewEnd-panStartViewStart); } viewStart=start; viewEnd=end; int maxTop=Math.Max(0,Height-ViewportHeight),wantedTop=Math.Max(-maxTop,Math.Min(0,panStartTop+screen.Y-panStartY)); verticalFraction=maxTop<=0?0:-wantedTop/(double)maxTop; SyncScale(); if(ViewChanged!=null)ViewChanged(this,EventArgs.Empty); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if(!panning)return; panning=false; Capture=false; Cursor=Cursors.Hand; }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if(!panning) { hoveredSlice=null; hoveredMarker=null; hoverTip.Hide(this); hoverTip.SetToolTip(this,""); } }
    protected override void Dispose(bool disposing) { if(disposing)hoverTip.Dispose(); base.Dispose(disposing); }
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
    void DrawLegend(Graphics graphics,string label,Color color,int x,int y,int scale,Font font) {
        using(Brush brush=new SolidBrush(color))graphics.FillEllipse(brush,x,y+3*scale,8*scale,8*scale);
        TextRenderer.DrawText(graphics,label,font,new Point(x+12*scale,y),PaceTheme.Muted,TextFormatFlags.NoPadding);
    }
    void DrawMarkerLegend(Graphics graphics,string label,Color color,int x,int y,int scale,Font font) {
        using(Pen pen=new Pen(color,2*scale))graphics.DrawLine(pen,x+4*scale,y+1*scale,x+4*scale,y+12*scale);
        TextRenderer.DrawText(graphics,label,font,new Point(x+12*scale,y),PaceTheme.Muted,TextFormatFlags.NoPadding);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color ink=PaceTheme.Text,muted=PaceTheme.Muted; int scale=Math.Max(1,ZoomLevel),logicalWidth=Math.Max(20,baseWidth);
        using(Font scaledFont=new Font(Font.FontFamily,Font.Size*scale,Font.Style))
        using(Font heading=new Font("Segoe UI Semibold",9*scale)) {
        TextRenderer.DrawText(e.Graphics,day==DateTime.Today?"TODAY'S TRACKING":day.ToString("ddd, MMM d").ToUpperInvariant(),heading,new Point(10*scale,3*scale),ink,TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics,Duration(TrackedSeconds)+" tracked",scaledFont,new Rectangle(150*scale,3*scale,Math.Max(20,(logicalWidth-160)*scale),18*scale),muted,TextFormatFlags.Right|TextFormatFlags.NoPadding);
        Rectangle lane=ScaledLane();
        using(Brush brush=new SolidBrush(Empty))FillRound(e.Graphics,brush,lane,8*scale);
        GraphicsState laneState=e.Graphics.Save(); using(GraphicsPath lanePath=RoundPath(lane,8*scale))e.Graphics.SetClip(lanePath);
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
        if(now>=fullStart && now<=fullEnd && day==DateTime.Today) {
            int currentX=TimeX(now,lane); using(Pen pen=new Pen(ink,2*scale))e.Graphics.DrawLine(pen,currentX,lane.Top-3*scale,currentX,lane.Bottom+3*scale);
        }
        foreach(Marker marker in markers) {
            int x=TimeX(marker.At,lane); Color color=marker.Kind=="BreakCanceled"?breakCanceled:breakEndedEarly;
            using(Pen pen=new Pen(color,2*scale)) { e.Graphics.DrawLine(pen,x,lane.Bottom+2*scale,x,lane.Bottom+11*scale); e.Graphics.DrawLine(pen,x-3*scale,lane.Bottom+11*scale,x+3*scale,lane.Bottom+11*scale); }
        }
        int divisions=4;
        for(int i=0;i<=divisions;i++) {
            DateTime labelTime=fullStart.AddTicks((long)((fullEnd-fullStart).Ticks*i/(double)divisions)); string label=labelTime.ToString("h:mmtt").ToLowerInvariant();
            int x=lane.Left+(int)Math.Round(lane.Width*i/(double)divisions),width=52*scale;
            if(i==0)x=lane.Left; else if(i==divisions)x=lane.Right-width; else x-=width/2;
            TextRenderer.DrawText(e.Graphics,label,scaledFont,new Rectangle(x,59*scale,width,15*scale),muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPadding);
        }
        int legendY=Math.Max(63,baseHeight-18)*scale;
        DrawLegend(e.Graphics,"Tracked",tracked,10*scale,legendY,scale,scaledFont);
        DrawLegend(e.Graphics,"Paused",manualPaused,80*scale,legendY,scale,scaledFont);
        DrawLegend(e.Graphics,"Break",breakColor,145*scale,legendY,scale,scaledFont);
        DrawLegend(e.Graphics,"Offscreen",offscreen,200*scale,legendY,scale,scaledFont);
        DrawLegend(e.Graphics,"Locked / sleep",locked,285*scale,legendY,scale,scaledFont);
        DrawLegend(e.Graphics,"Not tracked",gap,395*scale,legendY,scale,scaledFont);
        DrawMarkerLegend(e.Graphics,"Canceled",breakCanceled,495*scale,legendY,scale,scaledFont);
        DrawMarkerLegend(e.Graphics,"Left early",breakEndedEarly,585*scale,legendY,scale,scaledFont);
        }
    }
}

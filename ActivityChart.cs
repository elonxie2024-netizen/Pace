using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

public sealed class ActivityChart : Control {
    readonly Color[] colors={Color.FromArgb(89,145,121),Color.FromArgb(110,145,173),Color.FromArgb(211,151,107),Color.FromArgb(151,125,169),Color.FromArgb(154,166,142)};
    readonly List<KeyValuePair<string,double>> data=new List<KeyValuePair<string,double>>();
    public ActivityChart() { DoubleBuffered=true; BackColor=Color.FromArgb(252,251,247); Font=new Font("Segoe UI",9); }
    public void SetData(IEnumerable<ActivityRecord> activities) { Dictionary<string,double> sums=new Dictionary<string,double>(); foreach(ActivityRecord a in activities) { string key=string.IsNullOrEmpty(a.Category)?"Other":a.Category; if(!sums.ContainsKey(key))sums[key]=0; sums[key]+=a.Seconds; } data.Clear(); foreach(var pair in sums)data.Add(pair); data.Sort((a,b)=>b.Value.CompareTo(a.Value)); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias; double total=0; foreach(var p in data)total+=p.Value; if(total<=0) { TextRenderer.DrawText(e.Graphics,"Activity visuals will appear here",Font,ClientRectangle,Color.FromArgb(91,108,100),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter); return; } Rectangle pie=new Rectangle(12,20,118,118); float angle=-90; for(int i=0;i<data.Count;i++) { float sweep=(float)(data[i].Value/total*360); using(Brush b=new SolidBrush(colors[i%colors.Length]))e.Graphics.FillPie(b,pie,angle,sweep); angle+=sweep; } int y=4; for(int i=0;i<data.Count;i++) { int x=150; string label=data[i].Key; double pct=data[i].Value/total; using(Brush b=new SolidBrush(colors[i%colors.Length]))e.Graphics.FillEllipse(b,x,y+4,10,10); TextRenderer.DrawText(e.Graphics,label,Font,new Point(x+16,y),Color.FromArgb(38,58,53)); Rectangle bar=new Rectangle(x+16,y+18,Math.Max(2,(int)(pct*170)),4); using(Brush b=new SolidBrush(colors[i%colors.Length]))e.Graphics.FillRectangle(b,bar); TextRenderer.DrawText(e.Graphics,Math.Round(pct*100)+"%",Font,new Point(x+195,y),Color.FromArgb(91,108,100)); y+=30; }
    }
}

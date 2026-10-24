using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Runtime.InteropServices;using System.Windows.Forms;
namespace TransportFever3Trainer {
class CaptionSurface:Panel {protected override void WndProc(ref Message m){if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}base.WndProc(ref m);}}
class CaptionText:Label {protected override void WndProc(ref Message m){if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}base.WndProc(ref m);}}
class WindowChrome:Form {
 readonly Panel caption=new CaptionSurface();readonly Label captionText=new CaptionText();readonly PillButton maximize=new PillButton();Size normalSize;int nativeSizeState,resizeEdge;Point resizeStart;Rectangle resizeBounds;
 [DllImport("user32.dll")]static extern bool IsZoomed(IntPtr h);
 [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
 [StructLayout(LayoutKind.Sequential)]struct PointNative {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)]struct MinMaxInfo {public PointNative Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
 public WindowChrome(){FormBorderStyle=FormBorderStyle.None;Padding=new Padding(4);DoubleBuffered=true;BackColor=Theme.Back;Icon=BrandAssets.ReadIcon();
  caption.Dock=DockStyle.Top;caption.Height=32;caption.BackColor=Theme.Back;Controls.Add(caption);
  captionText.ForeColor=Theme.Muted;captionText.Font=Theme.F(9);var logo=new BrandMark();logo.SetBounds(12,4,24,24);caption.Controls.Add(logo);captionText.SetBounds(44,7,680,25);caption.Controls.Add(captionText);
  var minimize=new PillButton{Text="—",BackColor=Theme.Back,Font=Theme.F(11),AccessibleName="最小化"};var close=new PillButton{Text="×",BackColor=Theme.Back,Font=Theme.F(17),AccessibleName="关闭"};maximize.Text="□";maximize.BackColor=Theme.Back;maximize.Font=Theme.F(13);maximize.AccessibleName="最大化或还原";
  caption.Controls.Add(minimize);caption.Controls.Add(maximize);caption.Controls.Add(close);
  Action layout=delegate{int right=caption.ClientSize.Width;minimize.SetBounds(right-132,0,42,32);maximize.SetBounds(right-88,0,42,32);close.SetBounds(right-44,0,42,32);captionText.Width=Math.Max(100,right-188);};caption.SizeChanged+=delegate{layout();};layout();
  minimize.Click+=delegate{WindowState=FormWindowState.Minimized;};maximize.Click+=delegate{ToggleMaximize();};close.Click+=delegate{Close();};

 }
 protected override void SetClientSizeCore(int x,int y){if(IsHandleCreated)SetWindowPos(Handle,IntPtr.Zero,0,0,x,y,0x16);else base.SetClientSizeCore(x,y);}
 public void SaveWindowImage(string path){using(var bitmap=new Bitmap(Width,Height)){using(var graphics=Graphics.FromImage(bitmap)){var dc=graphics.GetHdc();try{if(!PrintWindow(Handle,dc,0))throw new InvalidOperationException("窗口预览生成失败");}finally{graphics.ReleaseHdc(dc);}}bitmap.Save(path);}}
 public void ToggleMaximize(){WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
 protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);captionText.Text=Text;}
 protected override void OnResize(EventArgs e){base.OnResize(e);if(IsHandleCreated&&!IsZoomed(Handle)&&!IsIconic(Handle))normalSize=Size;if(maximize!=null)maximize.Text=IsHandleCreated&&IsZoomed(Handle)?"❐":"□";}
 protected override CreateParams CreateParams {get{var cp=base.CreateParams;cp.Style|=0x20000|0x10000|0x80000;return cp;}}
 static Point MessagePoint(IntPtr packed){long value=packed.ToInt64();return new Point(unchecked((short)(value&0xffff)),unchecked((short)((value>>16)&0xffff)));}
 protected override void WndProc(ref Message m){
  // Our client controls draw the entire title and frame. Keep native activation
  // bookkeeping while suppressing the system's nonclient title repaint.
  if(m.Msg==0x86){m.LParam=new IntPtr(-1);base.WndProc(ref m);return;}
  if(m.Msg==0x85){m.Result=IntPtr.Zero;return;}
  if(m.Msg==0xA1&&m.WParam.ToInt32()>=10&&m.WParam.ToInt32()<=17&&WindowState==FormWindowState.Normal){resizeEdge=m.WParam.ToInt32();resizeStart=MessagePoint(m.LParam);resizeBounds=Bounds;Capture=true;m.Result=IntPtr.Zero;return;}
  if(m.Msg==0x200&&resizeEdge!=0){var point=PointToScreen(MessagePoint(m.LParam));int dx=point.X-resizeStart.X,dy=point.Y-resizeStart.Y;int left=resizeBounds.Left,top=resizeBounds.Top,right=resizeBounds.Right,bottom=resizeBounds.Bottom;bool moveLeft=resizeEdge==10||resizeEdge==13||resizeEdge==16,moveRight=resizeEdge==11||resizeEdge==14||resizeEdge==17,moveTop=resizeEdge==12||resizeEdge==13||resizeEdge==14,moveBottom=resizeEdge==15||resizeEdge==16||resizeEdge==17;if(moveLeft)left=Math.Min(right-MinimumSize.Width,left+dx);if(moveRight)right=Math.Max(left+MinimumSize.Width,right+dx);if(moveTop)top=Math.Min(bottom-MinimumSize.Height,top+dy);if(moveBottom)bottom=Math.Max(top+MinimumSize.Height,bottom+dy);SetWindowPos(Handle,IntPtr.Zero,left,top,right-left,bottom-top,0x14);m.Result=IntPtr.Zero;return;}
  if((m.Msg==0x202||m.Msg==0x1f||m.Msg==0x215)&&resizeEdge!=0){resizeEdge=0;Capture=false;}
  if(m.Msg==0x83){m.Result=IntPtr.Zero;return;}
  bool restore=m.Msg==5&&m.WParam==IntPtr.Zero&&nativeSizeState!=0;Size targetSize=normalSize;if(m.Msg==5)nativeSizeState=m.WParam.ToInt32();
  base.WndProc(ref m);
  if(restore&&targetSize.Width>0&&!IsDisposed)BeginInvoke((Action)delegate{if(!IsDisposed&&!IsZoomed(Handle)&&!IsIconic(Handle))SetWindowPos(Handle,IntPtr.Zero,0,0,targetSize.Width,targetSize.Height,0x16);});
  if(m.Msg==0x24){var info=(MinMaxInfo)Marshal.PtrToStructure(m.LParam,typeof(MinMaxInfo));var screen=Screen.FromHandle(Handle);var work=screen.WorkingArea;info.MaxPosition.X=work.Left-screen.Bounds.Left;info.MaxPosition.Y=work.Top-screen.Bounds.Top;info.MaxSize.X=work.Width;info.MaxSize.Y=work.Height;Marshal.StructureToPtr(info,m.LParam,false);}
  else if(m.Msg==0x84&&WindowState==FormWindowState.Normal){long packed=m.LParam.ToInt64();var point=PointToClient(new Point(unchecked((short)(packed&0xffff)),unchecked((short)((packed>>16)&0xffff))));int edge=6;bool left=point.X<edge,right=point.X>=ClientSize.Width-edge,top=point.Y<edge,bottom=point.Y>=ClientSize.Height-edge;int hit=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:0;if(hit!=0)m.Result=new IntPtr(hit);else if(point.Y>=caption.Top&&point.Y<caption.Bottom&&point.X<caption.Right-132)m.Result=new IntPtr(2);}
 }
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var pen=new Pen(Theme.Line))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);}
}
class SlimScrollBar:Control {
 int contentSize,viewportSize,value,dragOffset;bool dragging,hovered;Orientation orientation=Orientation.Vertical;
 public event EventHandler ValueChanged;
 public SlimScrollBar(){Width=14;Height=14;ScrollStep=36;TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.ScrollBar;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Opaque|ControlStyles.Selectable,true);}
 public Orientation Orientation{get{return orientation;}set{orientation=value;Invalidate();}}
 public int ScrollStep{get;set;}
 bool Vertical{get{return orientation==Orientation.Vertical;}}
 int TrackLength{get{return Math.Max(0,(Vertical?Height:Width)-8);}}
 int Coordinate(Point p){return Vertical?p.Y:p.X;}
 public int ContentSize{get{return contentSize;}set{contentSize=Math.Max(0,value);SetValue(this.value);Invalidate();}}
 public int ViewportSize{get{return viewportSize;}set{viewportSize=Math.Max(0,value);SetValue(this.value);Invalidate();}}
 public int Maximum{get{return Math.Max(0,contentSize-viewportSize);}}
 public int Value{get{return value;}set{SetValue(value);}}
 void SetValue(int next){next=Math.Max(0,Math.Min(Maximum,next));if(value==next)return;value=next;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}
 public Rectangle ThumbBounds{get{int track=TrackLength;if(track==0)return Rectangle.Empty;int size=Math.Min(track,Math.Max(36,contentSize==0?track:(int)((long)track*viewportSize/contentSize)));int position=4+(Maximum==0?0:(int)((long)(track-size)*value/Maximum));int thickness=hovered||dragging?8:6;return Vertical?new Rectangle((Width-thickness)/2,position,thickness,size):new Rectangle(position,(Height-thickness)/2,size,thickness);}}
 protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Parent==null?Theme.Back:Parent.BackColor);if(Maximum==0||TrackLength<4)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var track=Vertical?new Rectangle((Width-4)/2,4,4,TrackLength):new Rectangle(4,(Height-4)/2,TrackLength,4);using(var p=Theme.Path(track,4))using(var b=new SolidBrush(Color.FromArgb(38,39,49)))e.Graphics.FillPath(b,p);var thumb=ThumbBounds;using(var p=Theme.Path(thumb,Vertical?thumb.Width:thumb.Height))using(var b=new SolidBrush(hovered||dragging?Color.FromArgb(151,138,255):Theme.Purple))e.Graphics.FillPath(b,p);}
 protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){hovered=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||Maximum==0)return;Focus();var thumb=ThumbBounds;if(thumb.Contains(e.Location)){dragging=true;dragOffset=Coordinate(e.Location)-(Vertical?thumb.Y:thumb.X);Capture=true;}else Value+=Coordinate(e.Location)<(Vertical?thumb.Y:thumb.X)?-viewportSize:viewportSize;Invalidate();}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!dragging)return;var thumb=ThumbBounds;int travel=TrackLength-(Vertical?thumb.Height:thumb.Width);if(travel>0)Value=(int)Math.Round((double)(Coordinate(e.Location)-4-dragOffset)*Maximum/travel);}
 protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;Invalidate();}
 protected override void OnMouseCaptureChanged(EventArgs e){dragging=false;Invalidate();base.OnMouseCaptureChanged(e);}
 protected override void OnMouseWheel(MouseEventArgs e){Value-=e.Delta/120*(SystemInformation.MouseWheelScrollLines<0?viewportSize:Math.Max(1,SystemInformation.MouseWheelScrollLines)*Math.Max(1,ScrollStep));var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;base.OnMouseWheel(e);}
 protected override bool IsInputKey(Keys keyData){var key=keyData&Keys.KeyCode;return key==Keys.Up||key==Keys.Down||key==Keys.Left||key==Keys.Right||key==Keys.Home||key==Keys.End||key==Keys.PageUp||key==Keys.PageDown||base.IsInputKey(keyData);}
 protected override void OnKeyDown(KeyEventArgs e){switch(e.KeyCode){case Keys.Up:case Keys.Left:Value-=Math.Max(1,ScrollStep);break;case Keys.Down:case Keys.Right:Value+=Math.Max(1,ScrollStep);break;case Keys.PageUp:Value-=viewportSize;break;case Keys.PageDown:Value+=viewportSize;break;case Keys.Home:Value=0;break;case Keys.End:Value=Maximum;break;default:base.OnKeyDown(e);return;}e.Handled=true;e.SuppressKeyPress=true;base.OnKeyDown(e);}
}
class ModernScrollPanel:Panel {
 readonly Panel viewport=new Panel();readonly SlimScrollBar bar=new SlimScrollBar();readonly Panel content;bool updating;int minimumHeight;
 public Panel Content{get{return content;}}public SlimScrollBar ScrollBar{get{return bar;}}public int Offset{get{return bar.Value;}}
 public int ContentHeight{get{return minimumHeight;}set{minimumHeight=Math.Max(0,value);RefreshViewport();}}
 public ModernScrollPanel():this(new Panel()){}
 public ModernScrollPanel(Panel inner){content=inner;DoubleBuffered=true;BackColor=Theme.Back;viewport.Dock=DockStyle.Fill;bar.Dock=DockStyle.Right;Controls.Add(viewport);Controls.Add(bar);Controls.SetChildIndex(viewport,0);Controls.SetChildIndex(bar,1);viewport.Controls.Add(content);content.AutoScroll=false;bar.ValueChanged+=delegate{content.Top=-bar.Value;};viewport.SizeChanged+=delegate{RefreshViewport();};content.Layout+=delegate{RefreshViewport();};content.ControlAdded+=delegate{RefreshViewport();};content.ControlRemoved+=delegate{RefreshViewport();};content.MouseWheel+=Wheel;viewport.MouseWheel+=Wheel;MouseWheel+=Wheel;}
 public void ScrollTo(int offset){bar.Value=offset;}
 void Wheel(object sender,MouseEventArgs e){var handled=e as HandledMouseEventArgs;if(handled!=null&&handled.Handled)return;bar.Value-=e.Delta/120*(SystemInformation.MouseWheelScrollLines<0?viewport.Height:Math.Max(1,SystemInformation.MouseWheelScrollLines)*36);if(handled!=null)handled.Handled=true;}
 protected override void OnBackColorChanged(EventArgs e){base.OnBackColorChanged(e);if(viewport!=null)viewport.BackColor=BackColor;if(content!=null)content.BackColor=BackColor;Invalidate(true);}
 public void RefreshViewport(){if(updating||content==null||IsDisposed)return;updating=true;try{content.Width=Math.Max(0,viewport.ClientSize.Width);int bottom=minimumHeight;foreach(Control child in content.Controls)if(child.Visible)bottom=Math.Max(bottom,child.Bottom+content.Padding.Bottom);content.Height=Math.Max(viewport.ClientSize.Height,bottom);bar.ContentSize=content.Height;bar.ViewportSize=viewport.ClientSize.Height;content.Top=-bar.Value;bar.Invalidate();}finally{updating=false;}}
}
}

using System;using System.Drawing;using System.Runtime.InteropServices;using System.Windows.Forms;
namespace TransportFever3Trainer {
// The native list retains selection, checkboxes and keyboard behavior. Its system
// scrollbars sit outside a clipped viewport; matching themed controls drive it.
class ModernListView:ListView {
 internal ModernListHost ScrollHost;bool refreshPending;
 internal void RefreshScrollbars(){if(ScrollHost!=null)ScrollHost.RefreshScrollbars();}
 void ScheduleRefresh(){if(refreshPending||!IsHandleCreated||IsDisposed||ScrollHost==null)return;refreshPending=true;BeginInvoke((Action)delegate{refreshPending=false;if(!IsDisposed)RefreshScrollbars();});}
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);ScheduleRefresh();}
 protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==0x5||m.Msg==0x115||m.Msg==0x114||m.Msg==0x20A||m.Msg==0x20E||m.Msg==0x100||m.Msg==0x101||(m.Msg>=0x1000&&m.Msg<=0x10ff&&m.Msg!=0x1004&&m.Msg!=0x100E&&m.Msg!=0x101F))ScheduleRefresh();}
}
class ModernListHost:Panel {
 readonly Panel viewport=new Panel();readonly ModernListView list;readonly SlimScrollBar vertical=new SlimScrollBar{ScrollStep=1};readonly SlimScrollBar horizontal=new SlimScrollBar{Orientation=Orientation.Horizontal};bool updating;
 [StructLayout(LayoutKind.Sequential)]struct ScrollInfo{public int Size;public uint Mask;public int Min,Max;public uint Page;public int Position,TrackPosition;}
 [DllImport("user32.dll")]static extern bool GetScrollInfo(IntPtr window,int bar,ref ScrollInfo info);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);
 public SlimScrollBar VerticalBar{get{return vertical;}}public SlimScrollBar HorizontalBar{get{return horizontal;}}
 public ModernListView List{get{return list;}}
 public ModernListHost(ModernListView inner){list=inner;DoubleBuffered=true;BackColor=Theme.Card;viewport.BackColor=BackColor;Controls.Add(viewport);Controls.Add(vertical);Controls.Add(horizontal);viewport.Controls.Add(list);list.ScrollHost=this;vertical.AccessibleName="列表竖向滚动";horizontal.AccessibleName="列表横向滚动";vertical.ValueChanged+=delegate{ScrollNative(1,vertical);};horizontal.ValueChanged+=delegate{ScrollNative(0,horizontal);};SizeChanged+=delegate{RefreshScrollbars();};}
 ScrollInfo ReadScroll(int bar){var info=new ScrollInfo{Size=Marshal.SizeOf(typeof(ScrollInfo)),Mask=7};GetScrollInfo(list.Handle,bar,ref info);return info;}
 void SetRange(SlimScrollBar bar,ScrollInfo info,bool shown){bar.ContentSize=shown?Math.Max(0,info.Max-info.Min+1):0;bar.ViewportSize=shown?(int)info.Page:0;bar.Value=shown?info.Position-info.Min:0;}
 void ScrollNative(int axis,SlimScrollBar bar){if(updating||!list.IsHandleCreated)return;var info=ReadScroll(axis);int target=info.Min+bar.Value;int delta=target-info.Position;if(axis==1&&list.Items.Count>0)delta*=Math.Max(1,list.GetItemRect(0).Height);SendMessage(list.Handle,0x1014,axis==0?new IntPtr(delta):IntPtr.Zero,axis==1?new IntPtr(delta):IntPtr.Zero);RefreshScrollbars();list.Invalidate();}
 public void RefreshScrollbars(){if(updating||IsDisposed)return;updating=true;try{
  if(!list.IsHandleCreated){viewport.SetBounds(0,0,Math.Max(0,Width-14),Height);list.Bounds=viewport.ClientRectangle;vertical.SetBounds(Math.Max(0,Width-14),0,14,Height);horizontal.Visible=false;return;}
  for(int attempt=0;attempt<4;attempt++){
   long style=GetWindowLongPtr(list.Handle,-16).ToInt64();bool nativeV=(style&0x200000)!=0,nativeH=(style&0x100000)!=0;
   horizontal.Visible=nativeH;int viewWidth=Math.Max(0,ClientSize.Width-14),viewHeight=Math.Max(0,ClientSize.Height-(nativeH?14:0));
   viewport.SetBounds(0,0,viewWidth,viewHeight);vertical.SetBounds(viewWidth,0,14,viewHeight);horizontal.SetBounds(0,viewHeight,viewWidth,14);
   list.SetBounds(0,0,viewWidth+(nativeV?SystemInformation.VerticalScrollBarWidth:0),viewHeight+(nativeH?SystemInformation.HorizontalScrollBarHeight:0));
   long after=GetWindowLongPtr(list.Handle,-16).ToInt64();if((after&0x300000)==(style&0x300000))break;
  }
  long finalStyle=GetWindowLongPtr(list.Handle,-16).ToInt64();SetRange(vertical,ReadScroll(1),(finalStyle&0x200000)!=0);SetRange(horizontal,ReadScroll(0),(finalStyle&0x100000)!=0);
 }finally{updating=false;}}
}
}

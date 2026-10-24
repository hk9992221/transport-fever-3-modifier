using System;

using System.Drawing;

using System.Drawing.Drawing2D;

using System.Runtime.InteropServices;

using System.Windows.Forms;



namespace TransportFever3Trainer {

// Skin only the built-in spin buttons: native stepping, repeat, keyboard,

// validation and accessibility remain handled by NumericUpDown.

class ThemedNumericUpDown : NumericUpDown {

    SpinButtonSkin skin;
    public ThemedNumericUpDown(){BackColor=Theme.Input;ForeColor=Theme.Ink;}

    protected override void OnHandleCreated(EventArgs e) {

        base.OnHandleCreated(e);

        if (skin != null) return;

        foreach (Control child in Controls) {

            if (child.GetType().Name == "UpDownButtons") {

                skin = new SpinButtonSkin(this, child);

                break;

            }

        }

    }

    protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); RefreshSkin(); }

    protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); RefreshSkin(); }

    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); RefreshSkin(); }

    void RefreshSkin() { if (skin != null) skin.Refresh(); }

    protected override void Dispose(bool disposing) {

        if (disposing && skin != null) { skin.Dispose(); skin = null; }

        base.Dispose(disposing);

    }



    sealed class SpinButtonSkin : NativeWindow, IDisposable {

        readonly NumericUpDown owner;

        readonly Control buttons;

        int hovered, pressed;

        const int WM_PAINT=0xF, WM_ERASEBKGND=0x14, WM_PRINT=0x317, WM_PRINTCLIENT=0x318;

        const int WM_MOUSEMOVE=0x200, WM_MOUSELEAVE=0x2A3, WM_LBUTTONDOWN=0x201, WM_LBUTTONUP=0x202, WM_CAPTURECHANGED=0x215;

        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)] struct PaintStruct {

            public IntPtr Hdc;

            [MarshalAs(UnmanagedType.Bool)] public bool Erase;

            public NativeRect Rect;

            [MarshalAs(UnmanagedType.Bool)] public bool Restore;

            [MarshalAs(UnmanagedType.Bool)] public bool IncUpdate;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst=32)] public byte[] Reserved;

        }

        [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr handle, out PaintStruct paint);

        [DllImport("user32.dll")] static extern bool EndPaint(IntPtr handle, ref PaintStruct paint);

        public SpinButtonSkin(NumericUpDown owner, Control buttons) {

            this.owner=owner; this.buttons=buttons;

            buttons.HandleCreated+=HandleCreated; buttons.HandleDestroyed+=HandleDestroyed;

            if (buttons.IsHandleCreated) AssignHandle(buttons.Handle);

        }

        void HandleCreated(object sender, EventArgs e) { AssignHandle(buttons.Handle); Refresh(); }

        void HandleDestroyed(object sender, EventArgs e) { hovered=pressed=0; if (Handle!=IntPtr.Zero) ReleaseHandle(); }

        public void Refresh() { if (!owner.Enabled) hovered=pressed=0; if (!buttons.IsDisposed) buttons.Invalidate(); }

        public void Dispose() {

            buttons.HandleCreated-=HandleCreated; buttons.HandleDestroyed-=HandleDestroyed;

            if (Handle!=IntPtr.Zero) ReleaseHandle();

        }

        int Half(IntPtr packed) {

            int x=unchecked((short)(packed.ToInt64()&0xffff));

            int y=unchecked((short)((packed.ToInt64()>>16)&0xffff));

            return x<0||x>=buttons.Width||y<0||y>=buttons.Height?0:y<buttons.Height/2?1:2;

        }

        protected override void WndProc(ref Message m) {

            if (m.Msg==WM_PAINT) {

                PaintStruct paint; IntPtr dc=BeginPaint(Handle,out paint);

                try { if(dc!=IntPtr.Zero) using(var g=Graphics.FromHdc(dc)) Draw(g); }

                finally { EndPaint(Handle,ref paint); }

                m.Result=IntPtr.Zero; return;

            }

            if ((m.Msg==WM_PRINTCLIENT||m.Msg==WM_PRINT)&&m.WParam!=IntPtr.Zero) {

                using(var g=Graphics.FromHdc(m.WParam)) Draw(g);

                m.Result=IntPtr.Zero; return;

            }

            if (m.Msg==WM_ERASEBKGND) { m.Result=new IntPtr(1); return; }

            bool changed=false;

            if(m.Msg==WM_MOUSEMOVE) { int next=Half(m.LParam); changed=next!=hovered; hovered=next; }

            else if(m.Msg==WM_MOUSELEAVE) { hovered=0; changed=true; }

            else if(m.Msg==WM_LBUTTONDOWN) { pressed=hovered=Half(m.LParam); changed=true; }

            else if(m.Msg==WM_LBUTTONUP||m.Msg==WM_CAPTURECHANGED) { pressed=0; changed=true; }

            base.WndProc(ref m);

            if(changed&&!buttons.IsDisposed) buttons.Invalidate();

        }

        void Draw(Graphics g) {

            int w=buttons.ClientSize.Width,h=buttons.ClientSize.Height;

            if(w<1||h<2)return;

            bool enabled=owner.Enabled&&buttons.Enabled;

            using(var background=new SolidBrush(owner.BackColor)) g.FillRectangle(background,0,0,w,h);

            int split=h/2;

            if(enabled&&hovered!=0) {

                Rectangle area=hovered==1?new Rectangle(1,0,w-1,split):new Rectangle(1,split,w-1,h-split);

                using(var fill=new SolidBrush(pressed==hovered?Theme.Purple:Color.FromArgb(60,Theme.Purple))) g.FillRectangle(fill,area);

            }

            using(var line=new Pen(Color.FromArgb(55,Theme.Muted))) {

                g.DrawLine(line,0,1,0,h-2);

                g.DrawLine(line,3,split,w-3,split);

            }

            g.SmoothingMode=SmoothingMode.AntiAlias;

            for(int half=1;half<=2;half++) {

                float cx=(w+1)/2f,cy=half==1?split/2f:split+(h-split)/2f;

                float radius=Math.Min(3f,Math.Min(w/4f,split/3f));

                float direction=half==1?-1:1;

                Color ink=!enabled?Color.FromArgb(100,Theme.Muted):pressed==half&&hovered==half?Color.White:Theme.Muted;

                using(var pen=new Pen(ink,1.4f)) {

                    pen.StartCap=pen.EndCap=LineCap.Round; pen.LineJoin=LineJoin.Round;

                    g.DrawLines(pen,new[]{new PointF(cx-radius,cy-direction*radius/2),new PointF(cx,cy+direction*radius/2),new PointF(cx+radius,cy-direction*radius/2)});

                }

            }

        }

    }

}

}

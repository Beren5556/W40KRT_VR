using System;

namespace RTMaquetaXR
{
    internal struct TouchGuideIcon { internal string Symbol; internal float X,Y,Size; }
    internal struct TouchGuideStroke
    {
        internal float X0, Y0, X1, Y1, Width;
        internal int Tone;
    }

    // One small retained vector buffer, shared by the world canvas and flat
    // presentation. Coordinates are panel-local pixels, never pointer targets.
    internal sealed class TouchGuideAnimation
    {
        internal const float Width = 640, Height = 174;
        internal const float IconBody = 36, IconBandTop = 107, IconSlot = 67, IconGap = 8;
        internal const float PortraitSize = 94, PortraitTop = 5;
        internal readonly TouchGuideStroke[] Strokes = new TouchGuideStroke[1024];
        internal int Count { get; private set; }
        internal readonly TouchGuideIcon[] Icons=new TouchGuideIcon[32];
        internal int IconCount { get; private set; }
        internal float LeftPortraitX { get; private set; }
        internal float RightPortraitX { get; private set; }
        internal bool LeftPortraitActive { get; private set; }
        internal bool RightPortraitActive { get; private set; }
        internal void Build(TouchGuideGesture gesture, double seconds)
        {
            Count = 0; IconCount=0;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) seconds = 0;
            double loop = gesture == TouchGuideGesture.HeadMove ? 4.5 : gesture == TouchGuideGesture.EndTurn ? 6.0 : gesture == TouchGuideGesture.OverlayHold ? 2.0 : 3.0;
            double phase = (seconds % loop + loop) % loop / loop;
            float wave = (float)Math.Sin(phase * Math.PI * 2);
            float pulse = (float)(.5 - .5 * Math.Cos(phase * Math.PI * 2));
            float left = 176, right = 464, board = 320, depth = 0;
            if (gesture == TouchGuideGesture.TableMove) { left += wave * 20; board += wave * 20; }
            if (gesture == TouchGuideGesture.RotateZoom) { left -= wave * 22; right += wave * 22; }
            if (gesture == TouchGuideGesture.Tilt && phase >= .5) depth = wave * 15;
            LeftPortraitX = left; RightPortraitX = right;
            LeftPortraitActive = gesture == TouchGuideGesture.SecondaryClick || gesture == TouchGuideGesture.TableMove || gesture == TouchGuideGesture.OverlayHold ||
                gesture == TouchGuideGesture.RotateZoom || gesture == TouchGuideGesture.Tilt || gesture == TouchGuideGesture.Pause || gesture == TouchGuideGesture.DrawDistance ||
                gesture == TouchGuideGesture.RadialLeft || gesture == TouchGuideGesture.RadialRight || gesture == TouchGuideGesture.RadialInspect || gesture == TouchGuideGesture.ExplorationShortcuts || gesture == TouchGuideGesture.EndTurn || gesture == TouchGuideGesture.MapNavigation || gesture == TouchGuideGesture.WeaponComparison;
            RightPortraitActive = gesture != TouchGuideGesture.Tilt && gesture != TouchGuideGesture.KeyboardF1 &&
                gesture != TouchGuideGesture.Pause && gesture != TouchGuideGesture.TableMove;
            // Active-hand markers surround the original portrait; never dim
            // the skull itself to indicate availability.
            // A muted tabletop grid gives each animation a common spatial frame.
            Line(board - 60, 112 + depth, board + 60, 112 + depth, 1.5f, 0);
            Line(board + 60, 112 + depth, board + 88, 157 - depth, 1.5f, 0);
            Line(board + 88, 157 - depth, board - 88, 157 - depth, 1.5f, 0);
            Line(board - 88, 157 - depth, board - 60, 112 + depth, 1.5f, 0);
            for (int n = -1; n <= 1; ++n) Line(board + n * 30, 112 + depth, board + n * 44, 157 - depth, 1, 0);
            Line(board - 74, 134, board + 74, 134, 1, 0);
            // Shared glyph family, including the controls that the previous
            // animated drawings represented only as generic circles.
            switch(gesture) {
                case TouchGuideGesture.PointClick:case TouchGuideGesture.DragSelection:Icon("RT:PRESS",right+30,106,42);break;
                case TouchGuideGesture.HeadMove:case TouchGuideGesture.HeadView:Icon("RT:PRESS",right+30,103,46);break;
                case TouchGuideGesture.TableMove:Icon("LG:PRESS",left-25,108,48);break;
                case TouchGuideGesture.RotateZoom:Icon("LG:PRESS",left-25,108,48);Icon("RG:PRESS",right-25,108,48);break;
                case TouchGuideGesture.Tilt:Icon(phase<.5?"L:X":"L:Y",left-22,110,44);break;
                case TouchGuideGesture.OverlayHold:Icon("LG:PRESS",left-44,110,38);Icon("LT:PRESS",left+3,110,38);Icon("RG:PRESS",right-44,110,38);Icon("RT:PRESS",right+3,110,38);break;
                case TouchGuideGesture.Pause:Icon("X:PRESS",left-20,108,42);break;
                case TouchGuideGesture.SecondaryClick:Icon("LT:PRESS",left-20,110,42);Icon("B:PRESS",right-20,110,42);break;
                case TouchGuideGesture.KeyboardF1:Icon("F1",board-36,24,72);break;
                case TouchGuideGesture.DrawDistance:Icon("REST:PRESS",left-20,108,42);break;
                case TouchGuideGesture.RadialInspect:
                case TouchGuideGesture.EndTurn:
                case TouchGuideGesture.RadialLeft:Icon("LG:PRESS",left-44,117,36);Icon("LT:PRESS",left,117,36);break;
                case TouchGuideGesture.RadialRight:Icon("RG:PRESS",right-44,117,36);Icon("RT:PRESS",right,117,36);break;
                case TouchGuideGesture.OverlayNavigate:
                case TouchGuideGesture.InterfaceSettings:Icon("A:PRESS",right-44,117,36);Icon("RT:PRESS",right,117,36);Icon("R:XY",right+44,117,36);break;
                case TouchGuideGesture.NativePanels:Icon("RT:PRESS",right-44,117,36);Icon("B:PRESS",right,117,36);break;
                case TouchGuideGesture.WeaponComparison:Icon("Y:PRESS",left-20,112,42);break;
            }
            switch (gesture)
            {
                case TouchGuideGesture.ExplorationShortcuts:
                    float shortcutHand = phase < .66 ? left : right;
                    Icon(phase < .33 ? "L:PRESS" : phase < .66 ? "Y:PRESS" : "R:PRESS",shortcutHand-20,110,40);
                    Arrow(shortcutHand,91,shortcutHand,110,2);
                    if (phase < .33)
                    {
                        Box(board-28,40,56,43,2,1);
                        Line(board-9,40,board-9,83,1.5f,2); Line(board+9,40,board+9,83,1.5f,2);
                        Ring(board+16,57,4,2,2,1);
                        // The full native map takes both sticks: right pans
                        // its contents, left controls zoom and rotation.
                        Icon("R:XY",right-20,112,40);
                        Arrow(right-18,157,right+18,157,2);
                        Icon("L:ROTATE",left+44,117,36);
                    }
                    else
                    {
                        Box(board-17,45,34,31,2,1); Ring(board,61,28+pulse*8,2,2,1);
                        for (int n=0;n<4;++n) { double a=n*Math.PI/2; Line(board+(float)Math.Sin(a)*39,61+(float)Math.Cos(a)*39,board+(float)Math.Sin(a)*46,61+(float)Math.Cos(a)*46,2,2); }
                    }
                    break;
                case TouchGuideGesture.RadialInspect:
                    Ring(board,109,31,2,0,1);
                    for (int n=0;n<6;++n)
                    {
                        double angle=n*Math.PI/3;
                        Ring(board+(float)Math.Sin(angle)*25,109-(float)Math.Cos(angle)*25,4,2,n==0?2:1,1);
                    }
                    if (phase < .85)
                    {
                        // RIGHT grip reveals the chosen native card. Its
                        // release hides it; the RIGHT stick scrolls while held.
                        Icon("RG:PRESS",right-44,117,36);
                        if (phase < 1.0/3.0) { Beam(right,board,84); }
                        else if (phase < 2.0/3.0) Icon("L:XY",left+44,117,36);
                        else { Icon("R:Y",right,117,36); }
                        // Fixed screen information: no connecting line to the
                        // wheel or controller, and no following the aim ray.
                        Box(board-15,24,85,61,2,1);
                        for(int n=0;n<4;++n) Line(board-5,36+n*12,board+60-n*5,36+n*12,2,2);
                    }
                    else { Icon("RG",right-44,117,36); }
                    break;
                case TouchGuideGesture.RadialRight:
                case TouchGuideGesture.RadialLeft:
                    bool rightWheel = gesture == TouchGuideGesture.RadialRight;
                    float owner = rightWheel ? right : left, pointer = rightWheel ? left : right;
                    Ring(board,61,35,2,0,1);
                    if (!rightWheel) Ring(board,61,59,2,0,1);
                    for (int n = 0; n < (rightWheel ? 6 : 12); ++n)
                    {
                        double angle = n * Math.PI / 3;
                        float radius = n < 6 ? 29 : 51;
                        float px = board + (float)Math.Sin(angle)*radius, py = 61-(float)Math.Cos(angle)*radius;
                        Ring(px,py,4,2,n==0?2:1,1);
                    }
                    if (!rightWheel && phase >= 2.0/3.0)
                    {
                        // A vertical press on the left stick switches mode.
                        // Two alternate panel symbols remain inside the wheel;
                        // ordinary sector selection/cross-click occupy the
                        // first two phases, as in the actual controls.
                        Icon("L:PRESS",owner+44,117,36);
                        Box(board-23,49,18,24,2,1);Ring(board+15,55,6,2,1,1);
                        Line(board+7,72,board+23,72,3,1);
                        Arrow(board-15,85,board+15,85,2);Arrow(board+15,36,board-15,36,2);
                    }
                    else if (phase < (rightWheel?.5:1.0/3.0))
                    {
                        // Stay on one sector while its dwell fills. Previously
                        // the animation swept sectors, contradicting the hold.
                        Icon(rightWheel?"R:XY":"L:XY",owner+44,117,36); Arrow(owner,111,board+20,98,2);
                        if (!rightWheel) Ring(board,phase < 1.0/6.0 ? 32 : 10,7,2,2,1);
                        double fill = Math.Min(1, phase * 3);
                        for (int n=0;rightWheel && n<20*fill;++n)
                        {
                            double a=n*Math.PI/10, b=Math.Min((n+1)*Math.PI/10,fill*Math.PI*2);
                            Line(board+(float)Math.Sin(a)*10,32-(float)Math.Cos(a)*10,
                                board+(float)Math.Sin(b)*10,32-(float)Math.Cos(b)*10,2.5f,2);
                        }
                        if (!rightWheel)
                        {
                            // Explicit A confirmation replaces ability dwell.
                            Icon("A:PRESS",right-20,117,40);
                        }
                    }
                    else { Beam(pointer,board,32); Icon(rightWheel?"LT:PRESS":"RT:PRESS",pointer-20,117,40); Ring(board,32,7+pulse*4,2,2,1); }
                    break;
                case TouchGuideGesture.PointClick:
                    Beam(right, board, 132); Ring(board, 132, 6 + pulse * 8, 2, 2, 1); break;
                case TouchGuideGesture.HeadMove:
                case TouchGuideGesture.HeadView:
                    Beam(right, board, 132); Ring(board, 128, 8, 2, 1, 1);
                    // One sustained deep press, with a filling hold indicator.
                    Ring(right + 47, 61, 10, 3, 2, Math.Max(.04f, Math.Min(1f, (float)phase * 1.5f)));
                    if (gesture == TouchGuideGesture.HeadMove)
                    {
                        // The ground-combat move uses 3 s; the character/ship
                        // portrait entry remains the separate 2 s example.
                        Arrow(board-55,145,board-7,134,2);
                        Line(right+31,88,right+63,88,3,2);
                    }
                    Box(board - 32, 36, 64, 42, 2, 2); Arrow(board, 114, board, 83, 2); break;
                case TouchGuideGesture.DragSelection:
                    float edge = 24 + pulse * 54;
                    Box(board - 36, 118, edge, 30, 2, 2); Beam(right, board - 36 + edge, 148);
                    Ring(board - 18, 132, 4, 2, 1, 1); Ring(board + 22, 132, 4, 2, 1, 1); break;
                case TouchGuideGesture.GroupMovement:
                    for (int n = -1; n <= 1; ++n) Ring(board + n * 23, 143 - pulse * 24, 4, 2, 1, 1);
                    Beam(right,board,70);
                    Arrow(board,91,board,48,2); Arrow(board,91,board,108,2);
                    Arrow(board,91,board-32,91,2); Arrow(board,91,board+32,91,2);
                    Icon("R:XY",right-20,117,40); break;
                case TouchGuideGesture.EndTurn:
                    Ring(board,65,32,2,0,1); Ring(board,65,20,2,1,1);
                    Box(board-10,62,20,19,2,1);
                    Line(board-7,71,board-2,71,3,2); Line(board+2,71,board+7,71,3,2);
                    {
                        // Three independent examples, one second each. Never
                        // illustrate stick + A as a mandatory combination.
                        int route=(int)(phase*3);
                        float fill=Math.Min(1,(float)(phase*3-route)*2);
                        if(route==0)Icon("L:XY",left+44,117,36);
                        else if(route==1){Beam(right,board,65);Icon("RT:PRESS",right-20,117,40);}
                        else Icon("A:PRESS",right-20,117,40);
                        Ring(board,65,38,3,2,fill);
                        Line(board-43,153,board-43+86*fill,153,4,2);
                    }
                    break;
                case TouchGuideGesture.MapNavigation:
                    Box(board-66,24,132,69,2,1);
                    for(int n=0;n<4;n++) { float mx=board-45+n*30, my=46+(n%2)*23; Ring(mx,my,4,2,2,1); if(n<3) Line(mx+5,my,mx+25,46+((n+1)%2)*23,1.5f,0); }
                    Icon("R:XY",right-20,112,40); Arrow(right-23,157,right+23,157,2);
                    Icon("L:XY",left-20,112,40); Arrow(board+83,84,board+83,38,2);
                    break;
                case TouchGuideGesture.WeaponComparison:
                    for(int n=0;n<2;n++)
                    {
                        float cx=board-60+n*68;
                        Box(cx,22,55,77,2,1); Line(cx+10,42,cx+45,31,4,2);
                        for(int j=0;j<3;j++) Line(cx+9,61+j*10,cx+42-j*5,61+j*10,2,j==1?2:1);
                    }
                    Beam(right,board+25,64);
                    break;
                case TouchGuideGesture.NativePanels:
                    Box(board-70,19,140,83,2,1);
                    for(int n=0;n<4;n++) Line(board-54,35+n*16,board+46-(n%2)*25,35+n*16,2,n==(int)(phase*4)?2:1);
                    Beam(right,board+38,51); Icon("R:Y",right+44,117,36);
                    Line(board+60,30,board+60,92,2,0); Line(board+60,40+pulse*30,board+60,55+pulse*30,4,2);
                    break;
                case TouchGuideGesture.InterfaceSettings:
                    Box(board-67,23,134,77,2,1);
                    for(int n=0;n<3;n++)
                    {
                        float sy=40+n*22; Line(board-52,sy,board+50,sy,2,0);
                        float knob=board-38+76*(n==1?pulse:.5f); Box(knob-3,sy-5,6,10,2,2);
                    }
                    Beam(right,board+10,62); break;
                case TouchGuideGesture.TableMove:
                    Arrow(left - 50, 101, left + 50, 101, 2); break;
                case TouchGuideGesture.RotateZoom:
                    Arrow(270, 66, 226 - pulse * 20, 66, 2); Arrow(370, 66, 414 + pulse * 20, 66, 2);
                    Ring(board, 82, 24, 2, 2, .78f); Arrow(board - 19, 99, board - 26, 91, 2); break;
                case TouchGuideGesture.Tilt:
                    if (phase < .5)
                    {
                        Arrow(left, 158, left - 28, 158, 2); Arrow(left, 158, left + 28, 158, 2);
                        Ring(board, 77, 24, 2, 2, .78f); Arrow(board - 19, 94, board - 26, 86, 2);
                    }
                    else { Arrow(board + 105, 114 + depth, board + 105, 156 - depth, 2); }
                    break;
                case TouchGuideGesture.SecondaryClick:
                    Beam(left, board, 132); Ring(board, 132, 6 + pulse * 8, 2, 2, 1); break;
                case TouchGuideGesture.Pause:
                    Line(board - 11, 40, board - 11, 82, 8, 1); Line(board + 11, 40, board + 11, 82, 8, 1); break;
                case TouchGuideGesture.OverlayHold:
                    Ring(left, 57, 47, 2, 2, Math.Min(1,(float)phase*2)); Ring(right, 57, 47, 2, 2, Math.Min(1,(float)phase*2));
                    break;
                case TouchGuideGesture.OverlayNavigate:
                    Box(board - 47, 30, 94, 62, 1.5f, 0);
                    for (int n = 0; n < 3; ++n) Line(board - 33, 44 + n * 17, board + 33, 44 + n * 17, n == (int)(phase * 3) ? 4 : 1.5f, n == (int)(phase * 3) ? 2 : 1);
                    Beam(right, board + 30, 44 + (int)(phase * 3) * 17); break;
                case TouchGuideGesture.KeyboardF1:
                    break; // The approved F1 keycap above is the sole key shape.
                case TouchGuideGesture.DrawDistance:
                    Stick(right, wave, false);
                    Line(board - 60 - pulse * 12, 118 + pulse * 20, board + 60 + pulse * 12, 118 + pulse * 20, 3, 2);
                    Arrow(board, 91, board, 43, 2); break;
            }
            // Hand identity is carried by the controller artwork and exact
            // LT/RT/LG/RG/L/R controls, not a second procedural letter family.
            ArrangePresentation();
        }
        void ArrangePresentation()
        {
            // Gesture diagrams keep their horizontal motion and timing, in a
            // separate upper band. Control combinations get real room below:
            // adding a button never shrinks its neighbours or covers a diagram.
            for (int i=0;i<Count;++i)
            {
                var stroke=Strokes[i];stroke.Y0=stroke.Y0*.60f+2;stroke.Y1=stroke.Y1*.60f+2;Strokes[i]=stroke;
            }
            if(LeftPortraitActive)PortraitCorners(LeftPortraitX);
            if(RightPortraitActive)PortraitCorners(RightPortraitX);
            int leftCount=0,rightCount=0;
            for(int i=0;i<IconCount;++i)
                if(Icons[i].Symbol!="F1") { if(Icons[i].X+Icons[i].Size*.5f<Width*.5f)++leftCount;else ++rightCount; }
            int leftIndex=0,rightIndex=0;
            for(int i=0;i<IconCount;++i)
            {
                var icon=Icons[i];float center;
                if(icon.Symbol=="F1")center=Width*.5f;
                else
                {
                    bool left=icon.X+icon.Size*.5f<Width*.5f;
                    int count=left?leftCount:rightCount,index=left?leftIndex++:rightIndex++;
                    float hand=left?LeftPortraitX:RightPortraitX;
                    center=hand+(index-(count-1)*.5f)*(IconSlot+IconGap);
                }
                icon.X=center-IconBody*.5f;icon.Y=IconBandTop+(IconSlot-IconBody)*.5f;icon.Size=IconBody;Icons[i]=icon;
            }
        }
        void PortraitCorners(float x)
        {
            for (int side = -1; side <= 1; side += 2)
                for (int vertical = -1; vertical <= 1; vertical += 2)
                {
                    float px = x + side * 49, py = 52 + vertical * 49;
                    Line(px,py,px-side*12,py,3,2); Line(px,py,px,py-vertical*12,3,2);
                }
        }
        void Beam(float skull, float x, float y) { Line(skull + 14, 70, x, y, 1.5f, 2); }
        void Stick(float x, float amount, bool upward)
        {
            Icon(x < Width*.5f ? "L:Y" : "R:Y",x-20,99,40);
            if (upward) Arrow(x + 28, 135, x + 28, 102, 2);
            else { Arrow(x + 28, 119, x + 28, 97, 2); Arrow(x + 28, 119, x + 28, 141, 2); }
        }
        void Icon(string symbol,float x,float y,float size)
        {
            if(IconCount>=Icons.Length)throw new InvalidOperationException("Guide icon budget exceeded");
            Icons[IconCount++]=new TouchGuideIcon{Symbol=symbol,X=x,Y=y,Size=size};
        }

        void Arrow(float x0, float y0, float x1, float y1, int tone)
        {
            Line(x0, y0, x1, y1, 2, tone);
            double angle = Math.Atan2(y1 - y0, x1 - x0);
            Line(x1, y1, x1 - (float)Math.Cos(angle - .6) * 9, y1 - (float)Math.Sin(angle - .6) * 9, 2, tone);
            Line(x1, y1, x1 - (float)Math.Cos(angle + .6) * 9, y1 - (float)Math.Sin(angle + .6) * 9, 2, tone);
        }
        void Box(float x, float y, float width, float height, float thick, int tone)
        {
            Line(x, y, x + width, y, thick, tone); Line(x + width, y, x + width, y + height, thick, tone);
            Line(x + width, y + height, x, y + height, thick, tone); Line(x, y + height, x, y, thick, tone);
        }
        void Ring(float x, float y, float radius, float width, int tone, float fraction)
        {
            const int segments = 24;
            for (int n = 0; n < segments; ++n)
            {
                if (n / (float)segments >= fraction) break;
                double a = -Math.PI * .5 + n * Math.PI * 2 / segments;
                double b = -Math.PI * .5 + Math.Min((n + 1) / (float)segments, fraction) * Math.PI * 2;
                Line(x + (float)Math.Cos(a) * radius, y + (float)Math.Sin(a) * radius,
                    x + (float)Math.Cos(b) * radius, y + (float)Math.Sin(b) * radius, width, tone);
            }
        }
        void Line(float x0, float y0, float x1, float y1, float width, int tone)
        {
            if (Count >= Strokes.Length) throw new InvalidOperationException("Guide vector budget exceeded");
            Strokes[Count++] = new TouchGuideStroke { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Width = Math.Max(width,2.6f), Tone = tone };
        }
    }
}

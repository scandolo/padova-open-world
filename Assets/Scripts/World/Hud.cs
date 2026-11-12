using System.Collections.Generic;
using UnityEngine;

namespace Padova.World
{
    /// <summary>
    /// Shared immediate-mode HUD look: macOS condensed system fonts (nothing shipped), shadowed text,
    /// rounded translucent panels and key caps. All coordinates are in the 1600 × 1000 virtual canvas.
    /// </summary>
    public static class Hud
    {
        public static readonly Color Ink=new(.03f,.05f,.06f,.78f),Gold=new(1,.80f,.25f),Cream=new(.98f,.95f,.88f),Muted=new(.78f,.82f,.82f),Money=new(.55f,.92f,.55f),Danger=new(1,.32f,.25f),Cool=new(.35f,.75f,1);
        static Font body;static Texture2D round,disc;static GUIStyle panel;
        static readonly Dictionary<(int,bool,bool,TextAnchor),GUIStyle> styles=new();
        public static void Begin(){GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1600f,Screen.height/1000f,1));}
        static Font Body=>body?body:body=Font.CreateDynamicFontFromOSFont(new[]{"Avenir Next Condensed","Avenir Next","Helvetica Neue","Arial"},32);
        static Font Display=>Body;
        public static GUIStyle Style(int size,bool bold=false,bool title=false,TextAnchor align=TextAnchor.UpperLeft)
        {
            var key=(size,bold,title,align);
            if(!styles.TryGetValue(key,out var s))
            {
                s=new GUIStyle(GUI.skin.label){font=title?Display:Body,fontSize=size,fontStyle=bold||title?FontStyle.Bold:FontStyle.Normal,alignment=align,wordWrap=false,clipping=TextClipping.Overflow,richText=true};
                s.padding=new RectOffset(0,0,0,0);styles[key]=s;
            }
            return s;
        }
        /// <summary>Text with a soft drop shadow so it reads over bright plaster and sky.</summary>
        public static void Text(Rect r,string text,int size,Color color,TextAnchor align=TextAnchor.UpperLeft,bool bold=false,bool title=false)
        {
            var s=Style(size,bold,title,align);var old=GUI.color;
            GUI.color=new Color(0,0,0,.55f*color.a);GUI.Label(new Rect(r.x+1.5f,r.y+2,r.width,r.height),text,s);
            GUI.color=color;GUI.Label(r,text,s);GUI.color=old;
        }
        public static float Width(string text,int size,bool bold=false,bool title=false)=>Style(size,bold,title).CalcSize(new GUIContent(text)).x;
        static Texture2D Rounded(int size,float radius)
        {
            var t=new Texture2D(size,size,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.DontSave};
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float dx=Mathf.Max(radius-x-.5f,0,x+.5f-(size-radius)),dy=Mathf.Max(radius-y-.5f,0,y+.5f-(size-radius));
                t.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(radius-Mathf.Sqrt(dx*dx+dy*dy)+.5f)));
            }
            t.Apply();return t;
        }
        /// <summary>Rounded translucent panel.</summary>
        public static void Panel(Rect r,Color c)
        {
            if(!round){round=Rounded(32,10);panel=new GUIStyle{normal={background=round},border=new RectOffset(12,12,12,12)};}
            if(Event.current.type!=EventType.Repaint)return;var old=GUI.color;GUI.color=c;panel.Draw(r,false,false,false,false);GUI.color=old;
        }
        public static void Fill(Rect r,Color c){var old=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        public static void Disc(Vector2 centre,float size,Color c)
        {
            if(!disc)disc=Rounded(64,32);var old=GUI.color;GUI.color=c;GUI.DrawTexture(new Rect(centre.x-size/2,centre.y-size/2,size,size),disc);GUI.color=old;
        }
        /// <summary>A key cap followed by an action, centred on <paramref name="centre"/>; returns its width.</summary>
        public static float Prompt(Vector2 centre,string key,string action,int size=22)
        {
            float kw=Mathf.Max(34,Width(key,size-2,true)+18),aw=Width(action,size,true),w=kw+aw+30,h=size+20;var r=new Rect(centre.x-w/2,centre.y-h/2,w,h);
            Panel(r,Ink);Panel(new Rect(r.x+7,r.y+7,kw,h-14),Cream);
            Text(new Rect(r.x+7,r.y+7,kw,h-14),key,size-2,new Color(.08f,.1f,.12f),TextAnchor.MiddleCenter,true);
            Text(new Rect(r.x+kw+19,r.y,aw+8,h),action,size,Color.white,TextAnchor.MiddleLeft,true);return w;
        }
        public static void Bar(Rect r,float value,Color c){Panel(r,new Color(0,0,0,.45f));if(value>0)Panel(new Rect(r.x,r.y,Mathf.Max(r.height,r.width*Mathf.Clamp01(value)),r.height),c);}
        /// <summary>Large centred banner (BUSTED, fare complete); <paramref name="t"/> is seconds since it appeared.</summary>
        public static void Banner(string title,string subtitle,Color color,float t)
        {
            float a=Mathf.Clamp01(t*4)*Mathf.Clamp01((4-t)*2);if(a<=0)return;float grow=1+.06f*Mathf.Clamp01(1-t*3);
            Fill(new Rect(0,360,1600,150),new Color(0,0,0,.35f*a));
            var c=color;c.a=a;Text(new Rect(0,365-6*(grow-1)*10,1600,100),title,Mathf.RoundToInt(86*grow),c,TextAnchor.MiddleCenter,false,true);
            if(subtitle!="")Text(new Rect(0,455,1600,40),subtitle,26,new Color(1,1,1,a),TextAnchor.MiddleCenter,true);
        }
    }
}

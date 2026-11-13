using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Padova.World
{
    /// <summary>The title screen and the pause menu: play, picture quality, volume, the controls on one page, quit.
    /// Arrow keys and Enter, or the mouse. Esc opens it in play and backs out of it. The world stands still behind it.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class GameMenu : MonoBehaviour
    {
        public OpenWorld Game;
        public CityAudio Sound;
        /// <summary>"title", "pause", "controls" or empty while playing.</summary>
        public string Screen = "";
        public int Selected;
        /// <summary>Times the player has started or resumed play; exposed for tests.</summary>
        public int Resumes { get; private set; }
        string back = "pause";
        InputAction escape, confirm, up, down, left, right;
        bool escapePressed, confirmPressed, upPressed, downPressed, leftPressed, rightPressed;
        readonly List<(string label, string value)> items = new List<(string, string)>();

        void Awake()
        {
            InputAction Button(string binding, System.Action action)
            {
                var a = new InputAction(binding, InputActionType.Button, binding);
                a.performed += _ => action();
                a.Enable();
                return a;
            }
            escape = Button("<Keyboard>/escape", () => escapePressed = true);
            confirm = Button("<Keyboard>/enter", () => confirmPressed = true);
            confirm.AddBinding("<Keyboard>/space");
            confirm.AddBinding("<Keyboard>/numpadEnter");
            up = Button("<Keyboard>/upArrow", () => upPressed = true); up.AddBinding("<Keyboard>/w");
            down = Button("<Keyboard>/downArrow", () => downPressed = true); down.AddBinding("<Keyboard>/s");
            left = Button("<Keyboard>/leftArrow", () => leftPressed = true); left.AddBinding("<Keyboard>/a");
            right = Button("<Keyboard>/rightArrow", () => rightPressed = true); right.AddBinding("<Keyboard>/d");
        }

        void Start()
        {
            Quality.Load(Game);
            if (Sound && !Harness.Launched) Sound.Volume = PlayerPrefs.GetFloat("padova.volume", .8f);
            // Test runs go straight into play; everyone else is welcomed by the title.
            if (!Harness.Launched) Open("title");
        }

        void OnDestroy()
        {
            escape?.Dispose(); confirm?.Dispose(); up?.Dispose(); down?.Dispose(); left?.Dispose(); right?.Dispose();
        }

        public void Open(string screen)
        {
            if (screen == "controls") back = Screen == "" ? "pause" : Screen;
            Screen = screen;
            Selected = 0;
            Game.Menu = true;
            Game.Player.ControlsPaused = true;
            if (Game.Driving) Game.Driving.Paused = true;
            if (Game.Plane) Game.Plane.Paused = true;
            Game.Player.FollowCamera.ReleaseCursor();
        }

        public void Close()
        {
            Screen = "";
            Game.Menu = false;
            Game.Player.ControlsPaused = false;
            if (Game.Driving) Game.Driving.Paused = false;
            if (Game.Plane) Game.Plane.Paused = false;
            Resumes++;
        }

        void Build()
        {
            items.Clear();
            if (Screen == "controls") { items.Add(("Back", "")); return; }
            items.Add((Screen == "title" ? "Play" : "Resume", ""));
            items.Add(("Picture", Quality.Names[Quality.Level]));
            items.Add(("Sound", Sound ? Mathf.RoundToInt(Sound.Volume * 100) + " %" : "off"));
            items.Add(("Controls", ""));
            if (Screen == "pause") items.Add(("Back to the market", ""));
            items.Add(("Quit", ""));
        }

        void Update()
        {
            bool esc = escapePressed, ok = confirmPressed, u = upPressed, d = downPressed, l = leftPressed, r = rightPressed;
            escapePressed = confirmPressed = upPressed = downPressed = leftPressed = rightPressed = false;
            if (Screen == "")
            {
                if (esc && !Game.MapOpen && !(Game.Activities && Game.Activities.MenuOpen)) Open("pause");
                return;
            }
            Build();
            if (u) Selected = (Selected + items.Count - 1) % items.Count;
            if (d) Selected = (Selected + 1) % items.Count;
            if (l) Change(-1);
            if (r) Change(1);
            if (ok) Choose(Selected);
            else if (esc)
            {
                if (Screen == "controls") { Screen = back; Selected = 0; }
                else if (Screen == "pause") Close();
            }
        }

        void Change(int step)
        {
            string label = items[Selected].label;
            if (label == "Picture") Quality.Apply(Game, (Quality.Level + step + Quality.Names.Length) % Quality.Names.Length);
            else if (label == "Sound" && Sound)
            {
                Sound.Volume = Mathf.Clamp01(Mathf.Round((Sound.Volume + step * .1f) * 10) / 10);
                if (!Harness.Launched) PlayerPrefs.SetFloat("padova.volume", Sound.Volume);
                Sound.Play(SoundBank.Blip, .6f);
            }
        }

        void Choose(int index)
        {
            if (index < 0 || index >= items.Count) return;
            switch (items[index].label)
            {
                case "Play": case "Resume": Close(); break;
                case "Picture": case "Sound": Selected = index; Change(1); break;
                case "Controls": Open("controls"); break;
                case "Back": Screen = back; Selected = 0; break;
                case "Back to the market":
                    if (Game.Mode == "fly") Game.EndFlight();
                    if (Game.Mode == "drive") { Game.Driving.Place(Game.Driving.transform.position, Game.Driving.transform.rotation); Game.ExitCar(); }
                    if (Game.Mode == "walk") Game.Player.Respawn();
                    Close();
                    break;
                case "Quit":
                    PlayerPrefs.Save();
                    Application.Quit();
                    break;
            }
        }

        void OnGUI()
        {
            if (Screen == "") return;
            var old = GUI.matrix;
            Hud.Begin();
            Build();
            Hud.Fill(new Rect(0, 0, 1600, 1000), new Color(.02f, .03f, .05f, Screen == "title" ? .62f : .7f));
            if (Screen == "controls") Controls();
            else
            {
                Hud.Text(new Rect(120, 130, 1100, 170), "PADOVA", 170, Hud.Cream, TextAnchor.UpperLeft, false, true);
                Hud.Text(new Rect(128, 305, 1100, 36), Screen == "title" ? "The whole city, from the municipal survey.  Walk it, drive it, fly over it." : "Paused", 26, Hud.Muted, TextAnchor.UpperLeft, true);
            }
            float y = Screen == "controls" ? 850 : 400;
            for (int i = 0; i < items.Count; i++)
            {
                var row = new Rect(120, y + i * 66, 560, 58);
                bool hover = row.Contains(Event.current.mousePosition);
                if (hover && Event.current.type == EventType.MouseMove) Selected = i;
                bool on = i == Selected;
                Hud.Panel(row, on ? new Color(1, 1, 1, .14f) : new Color(1, 1, 1, .05f));
                if (on) Hud.Fill(new Rect(row.x, row.y + 8, 5, row.height - 16), Hud.Gold);
                Hud.Text(new Rect(row.x + 26, row.y, 300, row.height), items[i].label, 30, on ? Color.white : Hud.Muted, TextAnchor.MiddleLeft, true);
                if (items[i].value != "") Hud.Text(new Rect(row.x + 250, row.y, 290, row.height), "‹  " + items[i].value + "  ›", 26, on ? Hud.Gold : Hud.Muted, TextAnchor.MiddleRight, true);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { Selected = i; Choose(i); }
            }
            Hud.Text(new Rect(120, 940, 1200, 26), "↑ ↓  choose      ← →  change      Enter  select" + (Screen == "title" ? "" : "      Esc  back"), 18, Hud.Muted, TextAnchor.UpperLeft, true);
            if (Screen != "controls") Hud.Text(new Rect(900, 940, 660, 26), "Comune di Padova / Regione Veneto · IODL 2.0   ·   © OpenStreetMap", 15, new Color(1, 1, 1, .45f), TextAnchor.UpperRight);
            GUI.matrix = old;
        }

        static readonly string[][] Foot = { new[] { "W A S D / arrows", "Walk" }, new[] { "Shift", "Sprint" }, new[] { "Space", "Jump" }, new[] { "E", "Take a parked car, or steal one" }, new[] { "Mouse drag", "Look around" }, new[] { "F", "Sightseeing flight" } };
        static readonly string[][] Wheel = { new[] { "W A S D / arrows", "Drive and steer" }, new[] { "Shift", "Turbo" }, new[] { "Space", "Handbrake" }, new[] { "Q", "Horn" }, new[] { "E", "Get out" }, new[] { "R", "Back on the road" } };
        static readonly string[][] Always = { new[] { "M", "City map and waypoints" }, new[] { "J", "Jobs: taxi, deliveries, races" }, new[] { "T", "Skip to the next part of the day" }, new[] { "C", "Camera" }, new[] { "H", "Key hints on / off" }, new[] { "Esc", "This menu" } };

        void Controls()
        {
            Hud.Text(new Rect(120, 90, 900, 90), "CONTROLS", 84, Hud.Cream, TextAnchor.UpperLeft, false, true);
            Column(120, "ON FOOT", Foot);
            Column(600, "AT THE WHEEL", Wheel);
            Column(1080, "ANY TIME", Always);
        }

        static void Column(float x, string title, string[][] rows)
        {
            Hud.Panel(new Rect(x, 220, 440, 80 + rows.Length * 62), new Color(1, 1, 1, .06f));
            Hud.Text(new Rect(x + 26, 236, 400, 44), title, 32, Hud.Gold, TextAnchor.UpperLeft, false, true);
            for (int i = 0; i < rows.Length; i++)
            {
                float y = 296 + i * 62;
                Hud.Text(new Rect(x + 26, y, 400, 28), rows[i][0], 21, Hud.Cream, TextAnchor.UpperLeft, true);
                Hud.Text(new Rect(x + 26, y + 26, 400, 28), rows[i][1], 20, Color.white, TextAnchor.UpperLeft);
            }
        }
    }
}

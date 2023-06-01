using UnityEngine;
using System.Text;
using System.Collections;
using UnityEngine.Assertions;

namespace Terminal
{
    public enum TerminalState
    {
        Close,
        OpenSmall,
        OpenFull
    }

    public class Terminal : MonoBehaviour
    {
        [Header("Window")]
        [Range(0, 1)]
        [SerializeField]
        public float MaxHeight = 0.8f;

        [SerializeField]
        [Range(0, 1)]
        public float SmallTerminalRatio = 0.33f;

        [Range(100, 1000)]
        [SerializeField]
        public float ToggleSpeed = 720;

        [SerializeField] public string ToggleHotkey = "`";
        [SerializeField] public string ToggleFullHotkey = "#`";
        [SerializeField] public int BufferSize = 512;

        [Header("Input")]
        [SerializeField] public Font ConsoleFont;
        [SerializeField] public string InputCaret = ">";
        [SerializeField] public bool ShowGUIButtons = false;
        [SerializeField] public bool RightAlignButtons = false;

        [Header("Theme")]
        [Range(0, 1)]
        [SerializeField] public float InputContrast = 0.0f;
        [Range(0, 1)]
        [SerializeField] public float InputAlpha = 0.5f;

        [SerializeField] public Color BackgroundColor = Color.black;
        [SerializeField] public Color ForegroundColor = Color.white;
        [SerializeField] public Color ShellColor = Color.white;
        [SerializeField] public Color InputColor = Color.cyan;
        [SerializeField] public Color WarningColor = Color.yellow;
        [SerializeField] public Color ErrorColor = Color.red;

        private TerminalState _state;
        private TextEditor _editorState;
        private bool _inputFix;
        private bool _moveCursor;
        private bool _initialOpen; // Used to focus on TextField when console opens
        private Rect _window;
        private float _currentOpenT;
        private float _openTarget;
        private float _realWindowSize;
        private string _commandText;
        private string _cachedCommandText;
        private Vector2 _scrollPosition;
        private GUIStyle _windowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _inputStyle;
        private Texture2D _backgroundTexture;
        private Texture2D _inputBackgroundTexture;

        public static CommandLog Buffer { get; private set; }
        public static CommandShell Shell { get; private set; }
        public static CommandHistory History { get; private set; }
        public static CommandAutocomplete Autocomplete { get; private set; }

        public static bool IssuedError {
            get { return Shell.IssuedErrorMessage != null; }
        }

        public bool IsClosed {
            get { return _state == TerminalState.Close && Mathf.Approximately(_currentOpenT, _openTarget); }
        }

        public static void Log(string format, params object[] message) {
            Log(TerminalLogType.ShellMessage, format, message);
        }

        public static void Log(TerminalLogType type, string format, params object[] message) {
            Buffer.HandleLog(string.Format(format, message), type);
        }

        public void SetState(TerminalState newState) {
            _inputFix = true;
            _cachedCommandText = _commandText;
            _commandText = "";

            switch (newState) {
                case TerminalState.Close: {
                    _openTarget = 0;
                    break;
                }
                case TerminalState.OpenSmall: {
                    _openTarget = Screen.height * MaxHeight * SmallTerminalRatio;
                    if (_currentOpenT > _openTarget) {
                        // Prevent resizing from OpenFull to OpenSmall if window y position
                        // is greater than OpenSmall's target
                        _openTarget = 0;
                        _state = TerminalState.Close;
                        return;
                    }
                    _realWindowSize = _openTarget;
                    _scrollPosition.y = int.MaxValue;
                    break;
                }
                case TerminalState.OpenFull:
                default: {
                    _realWindowSize = Screen.height * MaxHeight;
                    _openTarget = _realWindowSize;
                    break;
                }
            }

            _state = newState;
        }

        public void ToggleState(TerminalState new_state) {
            if (_state == new_state) {
                SetState(TerminalState.Close);
            } else {
                SetState(new_state);
            }
        }

        void OnEnable() {
            Buffer = new CommandLog(BufferSize);
            Shell = new CommandShell();
            History = new CommandHistory();
            Autocomplete = new CommandAutocomplete();

            // Hook Unity log events
            Application.logMessageReceivedThreaded += HandleUnityLog;
        }

        void OnDisable() {
            Application.logMessageReceivedThreaded -= HandleUnityLog;
        }

        void Start() {
            if (ConsoleFont == null) {
                ConsoleFont = Font.CreateDynamicFontFromOSFont("Courier New", 16);
                Debug.LogWarning("Command Console Warning: Please assign a font.");
            }

            _commandText = "";
            _cachedCommandText = _commandText;
            Assert.AreNotEqual(ToggleHotkey.ToLower(), "return", "Return is not a valid ToggleHotkey");

            SetupWindow();
            SetupInput();
            SetupLabels();

            Shell.RegisterCommands();

            if (IssuedError) {
                Log(TerminalLogType.Error, "Error: {0}", Shell.IssuedErrorMessage);
            }

            foreach (var command in Shell.Commands) {
                Autocomplete.Register(command.Key);
            }
        }

        void OnGUI() {
            if (Event.current.Equals(Event.KeyboardEvent(ToggleHotkey))) {
                SetState(TerminalState.OpenSmall);
                _initialOpen = true;
            } else if (Event.current.Equals(Event.KeyboardEvent(ToggleFullHotkey))) {
                SetState(TerminalState.OpenFull);
                _initialOpen = true;
            }

            if (ShowGUIButtons) {
                DrawGUIButtons();
            }

            if (IsClosed) {
                return;
            }

            HandleOpenness();
            _window = GUILayout.Window(88, _window, DrawConsole, "", _windowStyle);
        }

        void SetupWindow() {
            _realWindowSize = Screen.height * MaxHeight / 3;
            _window = new Rect(0, _currentOpenT - _realWindowSize, Screen.width, _realWindowSize);

            // Set background color
            _backgroundTexture = new Texture2D(1, 1);
            _backgroundTexture.SetPixel(0, 0, BackgroundColor);
            _backgroundTexture.Apply();

            _windowStyle = new GUIStyle();
            _windowStyle.normal.background = _backgroundTexture;
            _windowStyle.padding = new RectOffset(4, 4, 4, 4);
            _windowStyle.normal.textColor = ForegroundColor;
            _windowStyle.font = ConsoleFont;
        }

        void SetupLabels() {
            _labelStyle = new GUIStyle();
            _labelStyle.font = ConsoleFont;
            _labelStyle.normal.textColor = ForegroundColor;
            _labelStyle.wordWrap = true;
        }

        void SetupInput() {
            _inputStyle = new GUIStyle();
            _inputStyle.padding = new RectOffset(4, 4, 4, 4);
            _inputStyle.font = ConsoleFont;
            _inputStyle.fixedHeight = ConsoleFont.fontSize * 1.6f;
            _inputStyle.normal.textColor = InputColor;

            var dark_background = new Color();
            dark_background.r = BackgroundColor.r - InputContrast;
            dark_background.g = BackgroundColor.g - InputContrast;
            dark_background.b = BackgroundColor.b - InputContrast;
            dark_background.a = InputAlpha;

            _inputBackgroundTexture = new Texture2D(1, 1);
            _inputBackgroundTexture.SetPixel(0, 0, dark_background);
            _inputBackgroundTexture.Apply();
            _inputStyle.normal.background = _inputBackgroundTexture;
        }

        void DrawConsole(int Window2D) {
            GUILayout.BeginVertical();

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, false, false, GUIStyle.none, GUIStyle.none);
            GUILayout.FlexibleSpace();
            DrawLogs();
            GUILayout.EndScrollView();

            if (_moveCursor) {
                CursorToEnd();
                _moveCursor = false;
            }

            if (Event.current.Equals(Event.KeyboardEvent("escape"))) {
                SetState(TerminalState.Close);
            } else if (Event.current.Equals(Event.KeyboardEvent("return"))
                || Event.current.Equals(Event.KeyboardEvent("[enter]"))) {
                EnterCommand();
            } else if (Event.current.Equals(Event.KeyboardEvent("up"))) {
                _commandText = History.Previous();
                _moveCursor = true;
            } else if (Event.current.Equals(Event.KeyboardEvent("down"))) {
                _commandText = History.Next();
            } else if (Event.current.Equals(Event.KeyboardEvent(ToggleHotkey))) {
                ToggleState(TerminalState.OpenSmall);
            } else if (Event.current.Equals(Event.KeyboardEvent(ToggleFullHotkey))) {
                ToggleState(TerminalState.OpenFull);
            } else if (Event.current.Equals(Event.KeyboardEvent("tab"))) {
                CompleteCommand();
                _moveCursor = true; // Wait till next draw call
            }

            GUILayout.BeginHorizontal();

            if (InputCaret != "") {
                GUILayout.Label(InputCaret, _inputStyle, GUILayout.Width(ConsoleFont.fontSize));
            }

            GUI.SetNextControlName("command_text_field");
            _commandText = GUILayout.TextField(_commandText, _inputStyle);

            if (_inputFix && _commandText.Length > 0) {
                _commandText = _cachedCommandText; // Otherwise the TextField picks up the ToggleHotkey character event
                _inputFix = false;                  // Prevents checking string Length every draw call
            }

            if (_initialOpen) {
                GUI.FocusControl("command_text_field");
                _initialOpen = false;
            }

            if (ShowGUIButtons && GUILayout.Button("| run", _inputStyle, GUILayout.Width(Screen.width / 10))) {
                EnterCommand();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        void DrawLogs() {
            foreach (var log in Buffer.Logs) {
                _labelStyle.normal.textColor = GetLogColor(log.type);
                GUILayout.Label(log.message, _labelStyle);
            }
        }

        void DrawGUIButtons() {
            int size = ConsoleFont.fontSize;
            float x_position = RightAlignButtons ? Screen.width - 7 * size : 0;

            // 7 is the number of chars in the button plus some padding, 2 is the line height.
            // The layout will resize according to the font size.
            GUILayout.BeginArea(new Rect(x_position, _currentOpenT, 7 * size, size * 2));
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Small", _windowStyle)) {
                ToggleState(TerminalState.OpenSmall);
            } else if (GUILayout.Button("Full", _windowStyle)) {
                ToggleState(TerminalState.OpenFull);
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void HandleOpenness() {
            float dt = ToggleSpeed * Time.unscaledDeltaTime;

            if (_currentOpenT < _openTarget) {
                _currentOpenT += dt;
                if (_currentOpenT > _openTarget) _currentOpenT = _openTarget;
            } else if (_currentOpenT > _openTarget) {
                _currentOpenT -= dt;
                if (_currentOpenT < _openTarget) _currentOpenT = _openTarget;
            } else {
                if (_inputFix) {
                    _inputFix = false;
                }
                return; // Already at target
            }

            _window = new Rect(0, _currentOpenT - _realWindowSize, Screen.width, _realWindowSize);
        }

        void EnterCommand() {
            Log(TerminalLogType.Input, "{0}", _commandText);
            Shell.RunCommand(_commandText);
            History.Push(_commandText);

            if (IssuedError) {
                Log(TerminalLogType.Error, "Error: {0}", Shell.IssuedErrorMessage);
            }

            _commandText = "";
            _scrollPosition.y = int.MaxValue;
        }

        void CompleteCommand() {
            string head_text = _commandText;
            int format_width = 0;

            string[] completion_buffer = Autocomplete.Complete(ref head_text, ref format_width);
            int completion_length = completion_buffer.Length;

            if (completion_length != 0) {
                _commandText = head_text;
            }

            if (completion_length > 1) {
                // Print possible completions
                var log_buffer = new StringBuilder();

                foreach (string completion in completion_buffer) {
                    log_buffer.Append(completion.PadRight(format_width + 4));
                }

                Log("{0}", log_buffer);
                _scrollPosition.y = int.MaxValue;
            }
        }

        void CursorToEnd() {
            if (_editorState == null) {
                _editorState = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            }

            _editorState.MoveCursorToPosition(new Vector2(999, 999));
        }

        void HandleUnityLog(string message, string stack_trace, LogType type) {
            Buffer.HandleLog(message, stack_trace, (TerminalLogType)type);
            _scrollPosition.y = int.MaxValue;
        }

        Color GetLogColor(TerminalLogType type) {
            switch (type) {
                case TerminalLogType.Message: return ForegroundColor;
                case TerminalLogType.Warning: return WarningColor;
                case TerminalLogType.Input: return InputColor;
                case TerminalLogType.ShellMessage: return ShellColor;
                default: return ErrorColor;
            }
        }
    }
}

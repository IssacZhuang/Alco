using System.Collections.Generic;
using System.Collections.Concurrent;

namespace UnityToolBox
{
    public class CommandHistory
    {
        private List<string> _history = new List<string>();
        private int _position;

        public void Push(string command_string) {
            if (command_string == "") {
                return;
            }

            _history.Add(command_string);
            _position = _history.Count;
        }

        public string Next() {
            _position++;

            if (_position >= _history.Count) {
                _position = _history.Count;
                return "";
            }

            return _history[_position];
        }

        public string Previous() {
            if (_history.Count == 0) {
                return "";
            }

            _position--;

            if (_position < 0) {
                _position = 0;
            }

            return _history[_position];
        }

        public void Clear() {
            _history.Clear();
            _position = 0;
        }
    }
}

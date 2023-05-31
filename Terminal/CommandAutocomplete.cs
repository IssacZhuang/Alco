using System.Collections.Generic;

namespace Terminal
{
    public class CommandAutocomplete
    {
        private List<string> _knownWords = new List<string>();
        private List<string> _buffer = new List<string>();

        public void Register(string word) {
            _knownWords.Add(word.ToLower());
        }

        public string[] Complete(ref string text, ref int format_width) {
            string partial_word = EatLastWord(ref text).ToLower();
            string known;

            for (int i = 0; i < _knownWords.Count; i++) {
                known = _knownWords[i];

                if (known.StartsWith(partial_word)) {
                    _buffer.Add(known);

                    if (known.Length > format_width) {
                        format_width = known.Length;
                    }
                }
            }

            string[] completions = _buffer.ToArray();
            _buffer.Clear();

            text += PartialWord(completions);
            return completions;
        }

        string EatLastWord(ref string text) {
            int last_space = text.LastIndexOf(' ');
            string result = text.Substring(last_space + 1);

            text = text.Substring(0, last_space + 1); // Remaining (keep space)
            return result;
        }

        string PartialWord(string[] words) {
            if (words.Length == 0) {
                return "";
            }

            string first_match = words[0];
            int partial_length = first_match.Length;

            if (words.Length == 1) {
                return first_match;
            }

            foreach (string word in words) {
                if (partial_length > word.Length) {
                    partial_length = word.Length;
                }

                for (int i = 0; i < partial_length; i++) {
                    if (word[i] != first_match[i]) {
                        partial_length = i;
                    }
                }
            }
            return first_match.Substring(0, partial_length);
        }
    }
}

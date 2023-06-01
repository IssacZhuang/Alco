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

            string firstMatch = words[0];
            int partialLength = firstMatch.Length;

            if (words.Length == 1) {
                return firstMatch;
            }

            foreach (string word in words) {
                if (partialLength > word.Length) {
                    partialLength = word.Length;
                }

                for (int i = 0; i < partialLength; i++) {
                    if (word[i] != firstMatch[i]) {
                        partialLength = i;
                    }
                }
            }
            return firstMatch.Substring(0, partialLength);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.JpCv {
    /// <summary>
    /// Japanese C+V phonemizer.
    /// Reclist: vowels a e i o u, consonants b c ch d f g h j k l m n p r s sh t w z
    /// Each consonant is assumed to be recorded with all five vowels (ba be bi bo bu ...),
    /// vowels are recorded alone (a e i o u), and bare consonants (e.g. "n") may exist too.
    /// Accepts romaji or hiragana/katakana lyrics. One phoneme per note.
    /// </summary>
    [Phonemizer("Japanese CV Phonemizer", "JA CV", "custom", language: "JA")]
    public class JpCvPhonemizer : Phonemizer {
        private USinger singer;

        public override void SetSinger(USinger singer) {
            this.singer = singer;
        }

        // Consonants from the reclist, longest first so "ch"/"sh" win over "c"/"s".
        private static readonly string[] Consonants = {
            "ch", "sh", "b", "c", "d", "f", "g", "h", "j", "k", "l",
            "m", "n", "p", "r", "s", "t", "w", "z"
        };
        private static readonly string Vowels = "aeiou";

        // Spellings to try when the exact alias isn't in the voicebank.
        private static readonly Dictionary<string, string[]> Fallbacks = new Dictionary<string, string[]> {
            { "shi", new[] { "si", "sh" } },
            { "chi", new[] { "ti", "ci", "ch" } },
            { "tsu", new[] { "tu", "su" } },
            { "fu",  new[] { "hu" } },
            { "ji",  new[] { "zi" } },
            { "zi",  new[] { "ji" } },
            { "si",  new[] { "shi" } },
            { "ti",  new[] { "chi" } },
            { "hu",  new[] { "fu" } },
            { "wo",  new[] { "o" } },
            { "ya",  new[] { "a" } },
            { "yu",  new[] { "u" } },
            { "yo",  new[] { "o" } },
        };

        public override Result Process(Note[] notes, Note? prev, Note? next,
                                       Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var note = notes[0];
            string lyric = (note.lyric ?? "").Trim();
            string romaji = ToRomaji(lyric).ToLowerInvariant();

            string alias = FindAlias(lyric, romaji, note.tone);
            return new Result {
                phonemes = new[] { new Phoneme { phoneme = alias } }
            };
        }

        private string FindAlias(string rawLyric, string romaji, int tone) {
            foreach (var candidate in Candidates(rawLyric, romaji)) {
                if (singer != null && singer.TryGetMappedOto(candidate, tone, out var oto)) {
                    return oto.Alias;
                }
            }
            // Nothing matched: return the romaji so the missing alias is visible in OpenUtau.
            return romaji;
        }

        private IEnumerable<string> Candidates(string rawLyric, string romaji) {
            var seen = new HashSet<string>();
            IEnumerable<string> Gen() {
                yield return rawLyric;
                yield return romaji;

                if (Fallbacks.TryGetValue(romaji, out var fb)) {
                    foreach (var f in fb) yield return f;
                }

                // Split into consonant + vowel and try dropping glides (kya -> ka).
                if (SplitCv(romaji, out string c, out string v)) {
                    if (c.EndsWith("y") && c.Length > 1) {
                        string noY = c.Substring(0, c.Length - 1);
                        yield return noY + v;
                    }
                    // Last resort: vowel alone, then bare consonant.
                    if (v.Length > 0) yield return v;
                    if (c.Length > 0) yield return c;
                }
            }
            foreach (var cand in Gen()) {
                if (!string.IsNullOrEmpty(cand) && seen.Add(cand)) yield return cand;
            }
        }

        private static bool SplitCv(string s, out string c, out string v) {
            c = ""; v = "";
            if (string.IsNullOrEmpty(s)) return false;
            // vowel at the end
            if (Vowels.IndexOf(s[s.Length - 1]) >= 0) {
                v = s.Substring(s.Length - 1);
                c = s.Substring(0, s.Length - 1);
            } else {
                c = s;
            }
            return true;
        }

        // ---------- Kana -> romaji ----------

        private static string ToRomaji(string lyric) {
            if (string.IsNullOrEmpty(lyric)) return lyric;
            string hira = KataToHira(lyric);
            if (!hira.Any(IsKana)) return lyric; // already romaji / other

            var sb = new StringBuilder();
            for (int i = 0; i < hira.Length; i++) {
                // yoon: kya, shu, cho ... (consonant kana + small ya/yu/yo)
                if (i + 1 < hira.Length && "ゃゅょ".IndexOf(hira[i + 1]) >= 0 &&
                    Kana.TryGetValue(hira[i].ToString(), out var baseRom) && baseRom.EndsWith("i")) {
                    string stem = baseRom.Substring(0, baseRom.Length - 1);
                    char v = hira[i + 1] == 'ゃ' ? 'a' : hira[i + 1] == 'ゅ' ? 'u' : 'o';
                    // shi/chi/ji + ya -> sha/cha/ja
                    if (stem == "sh" || stem == "ch" || stem == "j") sb.Append(stem).Append(v);
                    else sb.Append(stem).Append('y').Append(v);
                    i++;
                    continue;
                }
                if (Kana.TryGetValue(hira[i].ToString(), out var r)) sb.Append(r);
                else sb.Append(hira[i]);
            }
            return sb.ToString();
        }

        private static bool IsKana(char c) => c >= '\u3041' && c <= '\u3096';

        private static string KataToHira(string s) {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s) {
                if (ch >= '\u30A1' && ch <= '\u30F6') sb.Append((char)(ch - 0x60));
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        private static readonly Dictionary<string, string> Kana = new Dictionary<string, string> {
            {"あ","a"},{"い","i"},{"う","u"},{"え","e"},{"お","o"},
            {"か","ka"},{"き","ki"},{"く","ku"},{"け","ke"},{"こ","ko"},
            {"が","ga"},{"ぎ","gi"},{"ぐ","gu"},{"げ","ge"},{"ご","go"},
            {"さ","sa"},{"し","shi"},{"す","su"},{"せ","se"},{"そ","so"},
            {"ざ","za"},{"じ","ji"},{"ず","zu"},{"ぜ","ze"},{"ぞ","zo"},
            {"た","ta"},{"ち","chi"},{"つ","tsu"},{"て","te"},{"と","to"},
            {"だ","da"},{"ぢ","ji"},{"づ","zu"},{"で","de"},{"ど","do"},
            {"な","na"},{"に","ni"},{"ぬ","nu"},{"ね","ne"},{"の","no"},
            {"は","ha"},{"ひ","hi"},{"ふ","fu"},{"へ","he"},{"ほ","ho"},
            {"ば","ba"},{"び","bi"},{"ぶ","bu"},{"べ","be"},{"ぼ","bo"},
            {"ぱ","pa"},{"ぴ","pi"},{"ぷ","pu"},{"ぺ","pe"},{"ぽ","po"},
            {"ま","ma"},{"み","mi"},{"む","mu"},{"め","me"},{"も","mo"},
            {"や","ya"},{"ゆ","yu"},{"よ","yo"},
            {"ら","ra"},{"り","ri"},{"る","ru"},{"れ","re"},{"ろ","ro"},
            {"わ","wa"},{"を","wo"},{"ん","n"},
            {"ぁ","a"},{"ぃ","i"},{"ぅ","u"},{"ぇ","e"},{"ぉ","o"},
        };
    }
}

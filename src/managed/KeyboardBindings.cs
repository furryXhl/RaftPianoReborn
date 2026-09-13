using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RaftPianoReborn
{
    [Serializable]
    public sealed class NoteKeyBinding
    {
        public string key;
        public int semitone;

        public NoteKeyBinding() { }

        public NoteKeyBinding(string keyName, int semitoneOffset)
        {
            key = keyName;
            semitone = semitoneOffset;
        }
    }

    [Serializable]
    public sealed class OctaveKeyBindings
    {
        public string minus3;
        public string minus2;
        public string minus1;
        public string plus1;
        public string plus2;
        public string plus3;
    }

    [Serializable]
    public sealed class ControlKeyBindings
    {
        public string sustain;
        public string reset;
        public string velocityUp;
        public string velocityDown;
    }

    [Serializable]
    public sealed class KeyboardBindingFile
    {
        public int version;
        public NoteKeyBinding[] notes;
        public OctaveKeyBindings octaves;
        public ControlKeyBindings controls;
    }

    internal sealed class ResolvedKeyboardBindings
    {
        public readonly int[] NoteOffsets;
        public readonly int[] LayerKeys;
        public readonly int[] ControlKeys;
        public readonly string Source;

        public ResolvedKeyboardBindings(int[] noteOffsets, int[] layerKeys, int[] controlKeys, string source)
        {
            NoteOffsets = noteOffsets;
            LayerKeys = layerKeys;
            ControlKeys = controlKeys;
            Source = source;
        }
    }

    internal static class KeyboardBindings
    {
        internal const string FileName = "KeyBindings.json";
        private const int Unbound = -1000;

        public static ResolvedKeyboardBindings LoadOrCreate(string path, out string notice)
        {
            KeyboardBindingFile defaults = CreateDefault();
            if (!File.Exists(path))
            {
                WriteDefault(path, defaults);
                ResolvedKeyboardBindings created = Resolve(defaults, "default configuration");
                notice = "Created default key configuration: " + path + " " + DescribeControls(created);
                return created;
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (IsIncomplete307File(json))
                {
                    WriteDefault(path, defaults);
                    ResolvedKeyboardBindings repaired = Resolve(defaults, "repaired default configuration");
                    notice = "Repaired the incomplete 3.0.7 key configuration: " + path + " " +
                        DescribeControls(repaired);
                    return repaired;
                }
                KeyboardBindingFile file = ParseConfiguration(json);
                ResolvedKeyboardBindings resolved = Resolve(file, path);
                notice = "Loaded key configuration: " + path + " " + DescribeControls(resolved);
                return resolved;
            }
            catch (Exception e)
            {
                notice = "Invalid " + FileName + "; using built-in defaults. " + e.Message;
                return Resolve(defaults, "built-in fallback");
            }
        }

        private static void WriteDefault(string path, KeyboardBindingFile file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, DefaultJson(file), new UTF8Encoding(false));
        }

        private static string DefaultJson(KeyboardBindingFile file)
        {
            StringBuilder json = new StringBuilder(2400);
            json.AppendLine("{");
            json.AppendLine("  \"version\": 1,");
            json.AppendLine("  \"notes\": [");
            for (int i = 0; i < file.notes.Length; ++i)
            {
                NoteKeyBinding note = file.notes[i];
                json.Append("    { \"key\": ").Append(JsonString(note.key))
                    .Append(", \"semitone\": ").Append(note.semitone).Append(" }");
                if (i + 1 < file.notes.Length) json.Append(',');
                json.AppendLine();
            }
            json.AppendLine("  ],");
            json.AppendLine("  \"octaves\": {");
            json.Append("    \"minus3\": ").Append(JsonString(file.octaves.minus3)).AppendLine(",");
            json.Append("    \"minus2\": ").Append(JsonString(file.octaves.minus2)).AppendLine(",");
            json.Append("    \"minus1\": ").Append(JsonString(file.octaves.minus1)).AppendLine(",");
            json.Append("    \"plus1\": ").Append(JsonString(file.octaves.plus1)).AppendLine(",");
            json.Append("    \"plus2\": ").Append(JsonString(file.octaves.plus2)).AppendLine(",");
            json.Append("    \"plus3\": ").Append(JsonString(file.octaves.plus3)).AppendLine();
            json.AppendLine("  },");
            json.AppendLine("  \"controls\": {");
            json.Append("    \"sustain\": ").Append(JsonString(file.controls.sustain)).AppendLine(",");
            json.Append("    \"reset\": ").Append(JsonString(file.controls.reset)).AppendLine(",");
            json.Append("    \"velocityUp\": ").Append(JsonString(file.controls.velocityUp)).AppendLine(",");
            json.Append("    \"velocityDown\": ").Append(JsonString(file.controls.velocityDown)).AppendLine();
            json.AppendLine("  }");
            json.AppendLine("}");
            return json.ToString();
        }

        private static string JsonString(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static bool IsIncomplete307File(string json)
        {
            StringBuilder compact = new StringBuilder(json.Length);
            for (int i = 0; i < json.Length; ++i)
                if (!Char.IsWhiteSpace(json[i])) compact.Append(json[i]);
            return compact.ToString() == "{\"version\":1}";
        }

        private static KeyboardBindingFile ParseConfiguration(string json)
        {
            Dictionary<string, object> root = RequireObject(JsonParser.Parse(json), "root");
            List<object> noteValues = RequireArray(RequireProperty(root, "notes"), "notes");
            NoteKeyBinding[] notes = new NoteKeyBinding[noteValues.Count];
            for (int i = 0; i < noteValues.Count; ++i)
            {
                Dictionary<string, object> note = RequireObject(noteValues[i], "notes[" + i + "]");
                notes[i] = new NoteKeyBinding(
                    RequireString(RequireProperty(note, "key"), "notes[" + i + "].key"),
                    RequireInt(RequireProperty(note, "semitone"), "notes[" + i + "].semitone"));
            }

            Dictionary<string, object> octaveValues =
                RequireObject(RequireProperty(root, "octaves"), "octaves");
            Dictionary<string, object> controlValues =
                RequireObject(RequireProperty(root, "controls"), "controls");
            return new KeyboardBindingFile
            {
                version = RequireInt(RequireProperty(root, "version"), "version"),
                notes = notes,
                octaves = new OctaveKeyBindings
                {
                    minus3 = ReadString(octaveValues, "minus3", "octaves"),
                    minus2 = ReadString(octaveValues, "minus2", "octaves"),
                    minus1 = ReadString(octaveValues, "minus1", "octaves"),
                    plus1 = ReadString(octaveValues, "plus1", "octaves"),
                    plus2 = ReadString(octaveValues, "plus2", "octaves"),
                    plus3 = ReadString(octaveValues, "plus3", "octaves")
                },
                controls = new ControlKeyBindings
                {
                    sustain = ReadString(controlValues, "sustain", "controls"),
                    reset = ReadString(controlValues, "reset", "controls"),
                    velocityUp = ReadString(controlValues, "velocityUp", "controls"),
                    velocityDown = ReadString(controlValues, "velocityDown", "controls")
                }
            };
        }

        private static object RequireProperty(Dictionary<string, object> value, string name)
        {
            object result;
            if (!value.TryGetValue(name, out result)) throw new InvalidDataException(name + " is missing.");
            return result;
        }

        private static string ReadString(Dictionary<string, object> value, string name, string parent)
        {
            return RequireString(RequireProperty(value, name), parent + "." + name);
        }

        private static Dictionary<string, object> RequireObject(object value, string name)
        {
            Dictionary<string, object> result = value as Dictionary<string, object>;
            if (result == null) throw new InvalidDataException(name + " must be a JSON object.");
            return result;
        }

        private static List<object> RequireArray(object value, string name)
        {
            List<object> result = value as List<object>;
            if (result == null) throw new InvalidDataException(name + " must be a JSON array.");
            return result;
        }

        private static string RequireString(object value, string name)
        {
            string result = value as string;
            if (result == null) throw new InvalidDataException(name + " must be a string.");
            return result;
        }

        private static int RequireInt(object value, string name)
        {
            if (!(value is long)) throw new InvalidDataException(name + " must be an integer.");
            long result = (long)value;
            if (result < Int32.MinValue || result > Int32.MaxValue)
                throw new InvalidDataException(name + " is outside the supported integer range.");
            return (int)result;
        }

        // Unity's JsonUtility silently discarded the arrays on the game's Mono runtime.
        // This small strict parser keeps the player-editable format self-contained and deterministic.
        private sealed class JsonParser
        {
            private readonly string text;
            private int index;

            private JsonParser(string json)
            {
                if (json == null) throw new InvalidDataException("The JSON text is empty.");
                text = json;
            }

            public static object Parse(string json)
            {
                JsonParser parser = new JsonParser(json);
                object value = parser.ParseValue();
                parser.SkipWhitespace();
                if (parser.index != parser.text.Length) parser.Fail("Unexpected trailing content");
                return value;
            }

            private object ParseValue()
            {
                SkipWhitespace();
                if (index >= text.Length) Fail("Expected a value");
                char c = text[index];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't') { ReadLiteral("true"); return true; }
                if (c == 'f') { ReadLiteral("false"); return false; }
                if (c == 'n') { ReadLiteral("null"); return null; }
                if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                Fail("Unexpected character '" + c + "'");
                return null;
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
                ++index;
                SkipWhitespace();
                if (Take('}')) return result;
                while (true)
                {
                    SkipWhitespace();
                    if (index >= text.Length || text[index] != '"') Fail("Expected an object property name");
                    string name = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    if (result.ContainsKey(name)) Fail("Duplicate property '" + name + "'");
                    result.Add(name, ParseValue());
                    SkipWhitespace();
                    if (Take('}')) return result;
                    Expect(',');
                }
            }

            private List<object> ParseArray()
            {
                List<object> result = new List<object>();
                ++index;
                SkipWhitespace();
                if (Take(']')) return result;
                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (Take(']')) return result;
                    Expect(',');
                }
            }

            private string ParseString()
            {
                Expect('"');
                StringBuilder result = new StringBuilder();
                while (index < text.Length)
                {
                    char c = text[index++];
                    if (c == '"') return result.ToString();
                    if (c < 0x20) Fail("Unescaped control character in string");
                    if (c != '\\')
                    {
                        result.Append(c);
                        continue;
                    }
                    if (index >= text.Length) Fail("Incomplete string escape");
                    char escape = text[index++];
                    switch (escape)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u': result.Append(ParseUnicodeEscape()); break;
                        default: Fail("Unsupported string escape '\\" + escape + "'"); break;
                    }
                }
                Fail("Unterminated string");
                return null;
            }

            private char ParseUnicodeEscape()
            {
                if (index + 4 > text.Length) Fail("Incomplete Unicode escape");
                int value = 0;
                for (int i = 0; i < 4; ++i)
                {
                    char c = text[index++];
                    int digit = c >= '0' && c <= '9' ? c - '0' :
                        c >= 'a' && c <= 'f' ? c - 'a' + 10 :
                        c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                    if (digit < 0) Fail("Invalid Unicode escape");
                    value = value * 16 + digit;
                }
                return (char)value;
            }

            private object ParseNumber()
            {
                int start = index;
                if (text[index] == '-') ++index;
                if (index >= text.Length) Fail("Incomplete number");
                if (text[index] == '0')
                {
                    ++index;
                }
                else
                {
                    if (text[index] < '1' || text[index] > '9') Fail("Invalid number");
                    while (index < text.Length && text[index] >= '0' && text[index] <= '9') ++index;
                }
                if (index < text.Length && (text[index] == '.' || text[index] == 'e' || text[index] == 'E'))
                    Fail("Only integer numbers are supported");
                long result;
                if (!Int64.TryParse(text.Substring(start, index - start),
                    System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out result))
                    Fail("Invalid integer");
                return result;
            }

            private void ReadLiteral(string value)
            {
                if (index + value.Length > text.Length ||
                    String.CompareOrdinal(text, index, value, 0, value.Length) != 0)
                    Fail("Invalid literal");
                index += value.Length;
            }

            private bool Take(char expected)
            {
                if (index < text.Length && text[index] == expected)
                {
                    ++index;
                    return true;
                }
                return false;
            }

            private void Expect(char expected)
            {
                if (!Take(expected)) Fail("Expected '" + expected + "'");
            }

            private void SkipWhitespace()
            {
                while (index < text.Length && Char.IsWhiteSpace(text[index])) ++index;
            }

            private void Fail(string message)
            {
                throw new InvalidDataException(message + " at character " + index + ".");
            }
        }

        private static ResolvedKeyboardBindings Resolve(KeyboardBindingFile file, string source)
        {
            if (file == null) throw new InvalidDataException("The JSON root is empty.");
            if (file.version != 1) throw new InvalidDataException("Only configuration version 1 is supported.");
            if (file.notes == null || file.notes.Length == 0) throw new InvalidDataException("notes must not be empty.");
            if (file.octaves == null) throw new InvalidDataException("octaves is missing.");
            if (file.controls == null) throw new InvalidDataException("controls is missing.");

            int[] noteOffsets = new int[256];
            for (int i = 0; i < noteOffsets.Length; ++i) noteOffsets[i] = Unbound;
            bool[] covered = new bool[32];
            HashSet<int> used = new HashSet<int>();
            for (int i = 0; i < file.notes.Length; ++i)
            {
                NoteKeyBinding note = file.notes[i];
                if (note == null) throw new InvalidDataException("notes contains an empty entry.");
                if (note.semitone < 0 || note.semitone > 31)
                    throw new InvalidDataException("notes[" + i + "].semitone must be between 0 and 31.");
                int vk = VirtualKey(note.key);
                if (!used.Add(vk)) throw Duplicate(note.key);
                noteOffsets[vk] = note.semitone;
                covered[note.semitone] = true;
            }
            for (int i = 0; i < covered.Length; ++i)
                if (!covered[i]) throw new InvalidDataException("No key is assigned to semitone " + i + ".");

            int[] layerKeys =
            {
                VirtualKey(file.octaves.minus3), VirtualKey(file.octaves.minus2),
                VirtualKey(file.octaves.minus1), VirtualKey(file.octaves.plus1),
                VirtualKey(file.octaves.plus2), VirtualKey(file.octaves.plus3)
            };
            for (int i = 0; i < layerKeys.Length; ++i)
                if (!used.Add(layerKeys[i])) throw Duplicate(KeyName(layerKeys[i]));

            // Native control order: reset, louder, softer, sustain.
            int[] controlKeys =
            {
                VirtualKey(file.controls.reset), VirtualKey(file.controls.velocityUp),
                VirtualKey(file.controls.velocityDown), VirtualKey(file.controls.sustain)
            };
            for (int i = 0; i < controlKeys.Length; ++i)
                if (!used.Add(controlKeys[i])) throw Duplicate(KeyName(controlKeys[i]));

            return new ResolvedKeyboardBindings(noteOffsets, layerKeys, controlKeys, source);
        }

        private static InvalidDataException Duplicate(string key)
        {
            return new InvalidDataException("Key '" + key + "' is assigned more than once.");
        }

        private static string DescribeControls(ResolvedKeyboardBindings value)
        {
            return "controls(reset=" + KeyName(value.ControlKeys[0]) +
                ",louder=" + KeyName(value.ControlKeys[1]) +
                ",softer=" + KeyName(value.ControlKeys[2]) +
                ",sustain=" + KeyName(value.ControlKeys[3]) + ")";
        }

        private static KeyboardBindingFile CreateDefault()
        {
            return new KeyboardBindingFile
            {
                version = 1,
                notes = new[]
                {
                    N("Z", 0), N("S", 1), N("X", 2), N("D", 3),
                    N("C", 4), N("V", 5), N("G", 6), N("B", 7),
                    N("H", 8), N("N", 9), N("J", 10), N("M", 11),
                    N("Q", 12), N("Comma", 12), N("2", 13), N("L", 13),
                    N("W", 14), N("Period", 14), N("3", 15), N("Semicolon", 15),
                    N("4", 16), N("Slash", 16), N("R", 17), N("5", 18),
                    N("T", 19), N("6", 20), N("Y", 21), N("7", 22),
                    N("U", 23), N("I", 24), N("9", 25), N("O", 26),
                    N("0", 27), N("P", 28), N("LeftBracket", 29),
                    N("Equals", 30), N("RightBracket", 31)
                },
                octaves = new OctaveKeyBindings
                {
                    minus3 = "Tab", minus2 = "CapsLock", minus1 = "LeftShift",
                    plus1 = "Backslash", plus2 = "Enter", plus3 = "RightShift"
                },
                controls = new ControlKeyBindings
                {
                    sustain = "Space", reset = "Home", velocityUp = "Up", velocityDown = "Down"
                }
            };
        }

        private static NoteKeyBinding N(string key, int semitone)
        {
            return new NoteKeyBinding(key, semitone);
        }

        private static int VirtualKey(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new InvalidDataException("A key name is empty.");
            string value = name.Trim();
            if (value.Length == 1)
            {
                char c = Char.ToUpperInvariant(value[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
            }

            int functionNumber;
            if (value.Length >= 2 && (value[0] == 'F' || value[0] == 'f') &&
                Int32.TryParse(value.Substring(1), out functionNumber) && functionNumber >= 1 && functionNumber <= 24)
                return 0x6F + functionNumber;

            int numeric;
            if (value.StartsWith("VK_0x", StringComparison.OrdinalIgnoreCase) &&
                Int32.TryParse(value.Substring(5), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out numeric) && numeric >= 1 && numeric <= 255)
                return numeric;

            int vk;
            if (NamedKeys.TryGetValue(value, out vk)) return vk;
            throw new InvalidDataException("Unknown key name '" + name + "'. See the key-name list in the manual.");
        }

        private static string KeyName(int vk)
        {
            foreach (KeyValuePair<string, int> pair in NamedKeys)
                if (pair.Value == vk) return pair.Key;
            return "VK_0x" + vk.ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static readonly Dictionary<string, int> NamedKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            {"MouseLeft", 0x01}, {"MouseRight", 0x02}, {"MouseMiddle", 0x04}, {"MouseX1", 0x05}, {"MouseX2", 0x06},
            {"Backspace", 0x08}, {"Tab", 0x09}, {"Enter", 0x0D}, {"Return", 0x0D},
            {"CapsLock", 0x14}, {"Escape", 0x1B}, {"Space", 0x20}, {"PageUp", 0x21}, {"PageDown", 0x22},
            {"End", 0x23}, {"Home", 0x24}, {"Left", 0x25}, {"Up", 0x26}, {"Right", 0x27}, {"Down", 0x28},
            {"Insert", 0x2D}, {"Delete", 0x2E},
            {"Numpad0", 0x60}, {"Numpad1", 0x61}, {"Numpad2", 0x62}, {"Numpad3", 0x63}, {"Numpad4", 0x64},
            {"Numpad5", 0x65}, {"Numpad6", 0x66}, {"Numpad7", 0x67}, {"Numpad8", 0x68}, {"Numpad9", 0x69},
            {"NumpadMultiply", 0x6A}, {"NumpadAdd", 0x6B}, {"NumpadSubtract", 0x6D},
            {"NumpadDecimal", 0x6E}, {"NumpadDivide", 0x6F},
            {"NumLock", 0x90}, {"ScrollLock", 0x91},
            {"LeftShift", 0xA0}, {"RightShift", 0xA1}, {"LeftCtrl", 0xA2}, {"RightCtrl", 0xA3},
            {"LeftAlt", 0xA4}, {"RightAlt", 0xA5},
            {"Semicolon", 0xBA}, {"Equals", 0xBB}, {"Comma", 0xBC}, {"Minus", 0xBD},
            {"Period", 0xBE}, {"Slash", 0xBF}, {"Backtick", 0xC0}, {"LeftBracket", 0xDB},
            {"Backslash", 0xDC}, {"RightBracket", 0xDD}, {"Quote", 0xDE}
        };
    }
}

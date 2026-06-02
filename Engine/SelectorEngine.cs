using PlaywrightPrototype.models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlaywrightPrototype.Engine
{
    /// <summary>
    /// Builds stable, intent-driven selectors from accessibility metadata.
    /// Rejects brittle patterns (nth-of-type, deep CSS chains).
    /// </summary>
    public static class SelectorEngine
    {
        private static readonly Regex BrittlePattern = new(
            @":nth-(of-type|child)|>\s*div\s*>\s*div|>\s*\w+\s*>\s*\w+\s*>\s*\w+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static ElementTarget BuildFromDomElement(JsonElement el)
        {
            var tag = el.GetProperty("tag").GetString() ?? "";
            var role = el.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "";
            var text = el.TryGetProperty("text", out var t) ? t.GetString()?.Trim() ?? "" : "";
            var ariaLabel = el.TryGetProperty("ariaLabel", out var al) ? al.GetString()?.Trim() ?? "" : "";
            var id = el.TryGetProperty("id", out var i) ? i.GetString()?.Trim() ?? "" : "";
            var testId = el.TryGetProperty("testId", out var ti) ? ti.GetString()?.Trim() ?? "" : "";
            var label = el.TryGetProperty("label", out var lb) ? lb.GetString()?.Trim() ?? "" : "";
            var placeholder = el.TryGetProperty("placeholder", out var ph) ? ph.GetString()?.Trim() ?? "" : "";
            var name = el.TryGetProperty("name", out var n) ? n.GetString()?.Trim() ?? "" : "";
            var href = el.TryGetProperty("href", out var h) ? h.GetString()?.Trim() ?? "" : "";
            var inputType = el.TryGetProperty("inputType", out var it) ? it.GetString()?.Trim() ?? "" : "";

            if (string.IsNullOrEmpty(role))
                role = InferRole(tag, inputType, href);

            var accessibleName = FirstNonEmpty(ariaLabel, label, text, placeholder, name);
            var intent = BuildIntent(tag, role, accessibleName, inputType, href);

            var target = new ElementTarget
            {
                Intent = intent,
                Role = role,
                Name = accessibleName,
                TestId = testId,
                ElementId = id,
                Label = label,
                Placeholder = placeholder,
                Tag = tag
            };

            target.FallbackChain = BuildFallbackChain(target, tag, role, accessibleName, testId, id, label, placeholder);
            return target;
        }

        public static ElementTarget BuildForHeading(HeadingInfo h)
        {
            var target = new ElementTarget
            {
                Intent = $"verify {h.Level} heading \"{Truncate(h.Text, 40)}\"",
                Role = "heading",
                Name = h.Text,
                Tag = h.Level
            };
            if (!string.IsNullOrWhiteSpace(h.Text))
                target.FallbackChain.Add($"role:heading:{h.Text}");
            target.FallbackChain.Add($"css:{h.Level}");
            return target;
        }

        public static ElementTarget BuildForLink(LinkInfo link)
        {
            var name = FirstNonEmpty(link.Text, link.Href);
            var target = new ElementTarget
            {
                Intent = $"interact with link \"{Truncate(name, 40)}\"",
                Role = "link",
                Name = name,
                Tag = "a"
            };
            if (!string.IsNullOrWhiteSpace(name))
                target.FallbackChain.Add($"role:link:{name}");
            if (!string.IsNullOrWhiteSpace(link.Text))
                target.FallbackChain.Add($"text:{link.Text}");
            target.FallbackChain.Add("css:a");
            return target;
        }

        public static ElementTarget BuildForButton(ButtonInfo btn)
        {
            var target = new ElementTarget
            {
                Intent = $"interact with button \"{Truncate(btn.Text, 40)}\"",
                Role = "button",
                Name = btn.Text,
                ElementId = btn.Id,
                Tag = "button"
            };
            if (!string.IsNullOrWhiteSpace(btn.Text))
                target.FallbackChain.Add($"role:button:{btn.Text}");
            if (!string.IsNullOrWhiteSpace(btn.Id))
                target.FallbackChain.Add($"id:{btn.Id}");
            target.FallbackChain.Add("css:button");
            return target;
        }

        public static ElementTarget BuildForInput(InputInfo input)
        {
            var label = FirstNonEmpty(input.HasLabel ? input.Name : "", input.HasAriaLabel ? input.Name : "", input.HasPlaceholder ? input.Name : "");
            var accessibleName = FirstNonEmpty(input.Name, input.Id, input.HasPlaceholder ? "placeholder field" : "");
            var target = new ElementTarget
            {
                Intent = $"type into {input.Type} field \"{Truncate(accessibleName, 30)}\"",
                Role = input.Type == "checkbox" ? "checkbox" : input.Type == "radio" ? "radio" : "textbox",
                Name = accessibleName,
                ElementId = input.Id,
                Label = label,
                Placeholder = input.HasPlaceholder ? input.Name : "",
                Tag = "input"
            };

            if (!string.IsNullOrWhiteSpace(input.Id))
                target.FallbackChain.Add($"id:{input.Id}");
            if (!string.IsNullOrWhiteSpace(label))
                target.FallbackChain.Add($"label:{label}");
            if (input.HasPlaceholder && !string.IsNullOrWhiteSpace(input.Name))
                target.FallbackChain.Add($"placeholder:{input.Name}");
            if (!string.IsNullOrWhiteSpace(input.Name))
                target.FallbackChain.Add($"css:input[name=\"{EscapeCssAttr(input.Name)}\"]");
            return target;
        }

        public static ElementTarget BuildGeneric(string intent, string role, string name, string cssFallback)
        {
            var target = new ElementTarget { Intent = intent, Role = role, Name = name };
            if (!string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(name))
                target.FallbackChain.Add($"role:{role}:{name}");
            if (!string.IsNullOrWhiteSpace(cssFallback) && !IsBrittle(cssFallback))
                target.FallbackChain.Add($"css:{cssFallback}");
            return target;
        }

        public static bool IsBrittle(string selector) =>
            string.IsNullOrWhiteSpace(selector) || BrittlePattern.IsMatch(selector);

        public static List<string> BuildFallbackChain(ElementTarget t, string tag, string role,
            string name, string testId, string id, string label, string placeholder)
        {
            var chain = new List<string>();

            if (!string.IsNullOrWhiteSpace(testId))
                chain.Add($"testid:{testId}");

            if (!string.IsNullOrWhiteSpace(id))
                chain.Add($"id:{id}");

            if (!string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(name))
                chain.Add($"role:{role}:{name}");

            if (!string.IsNullOrWhiteSpace(label))
                chain.Add($"label:{label}");

            if (!string.IsNullOrWhiteSpace(placeholder))
                chain.Add($"placeholder:{placeholder}");

            if (!string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(role))
                chain.Add($"text:{name}");

            // Safe semantic CSS only — single tag or simple attribute, never nth-*
            var safeCss = BuildSafeCss(tag, id, testId);
            if (!string.IsNullOrEmpty(safeCss) && !chain.Any(c => c.EndsWith(safeCss)))
                chain.Add($"css:{safeCss}");

            return chain.Distinct().ToList();
        }

        private static string BuildSafeCss(string tag, string id, string testId)
        {
            if (!string.IsNullOrWhiteSpace(testId))
                return $"[data-testid=\"{EscapeCssAttr(testId)}\"]";
            if (!string.IsNullOrWhiteSpace(id))
                return $"#{EscapeCssAttr(id)}";
            if (!string.IsNullOrWhiteSpace(tag))
                return tag.ToLowerInvariant();
            return "";
        }

        private static string InferRole(string tag, string inputType, string href)
        {
            tag = tag.ToUpperInvariant();
            if (tag == "A") return "link";
            if (tag == "BUTTON") return "button";
            if (tag == "INPUT")
            {
                return inputType switch
                {
                    "checkbox" => "checkbox",
                    "radio" => "radio",
                    "submit" or "button" => "button",
                    _ => "textbox"
                };
            }
            if (tag is "SELECT") return "combobox";
            if (tag is "TEXTAREA") return "textbox";
            if (tag is "H1" or "H2" or "H3" or "H4" or "H5" or "H6") return "heading";
            if (tag == "IMG") return "img";
            if (!string.IsNullOrEmpty(href)) return "link";
            return "";
        }

        private static string BuildIntent(string tag, string role, string name, string inputType, string href)
        {
            var n = Truncate(name, 50);
            if (role == "link") return $"click link \"{n}\"";
            if (role == "button") return $"click button \"{n}\"";
            if (role == "textbox") return $"type into field \"{n}\"";
            if (role == "heading") return $"verify heading \"{n}\"";
            if (role == "checkbox") return $"toggle checkbox \"{n}\"";
            if (tag.Equals("select", StringComparison.OrdinalIgnoreCase)) return $"select option in \"{n}\" dropdown";
            if (ContainsAny(name, "cookie", "consent", "gdpr"))
                return $"dismiss cookie banner \"{n}\"";
            if (ContainsAny(name, "close", "dismiss", "accept", "decline"))
                return $"click {n.ToLowerInvariant()} button";
            return $"interact with {role ?? tag.ToLowerInvariant()} \"{n}\"";
        }

        private static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";

        private static bool ContainsAny(string text, params string[] terms) =>
            !string.IsNullOrEmpty(text) && terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

        private static string EscapeCssAttr(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

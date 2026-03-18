using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HcmcRainVision.Backend.Services.Chatbot
{
    public static class VietnameseTextNormalizer
    {
        public static string NormalizeForIntent(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var text = input.Trim().ToLowerInvariant();
            text = text.Replace("->", " den ");
            text = text.Replace("→", " den ");
            text = RemoveDiacritics(text);

            text = Regex.Replace(text, @"\bko\b|\bkhongg\b|\bhong\b", "khong");
            text = Regex.Replace(text, @"\bdc\b|\bduoc\b", "duoc");
            text = Regex.Replace(text, @"\bdg\b|\bduong\b", "duong");
            text = Regex.Replace(text, @"\btui\b|\bminh\b", "toi");
            text = Regex.Replace(text, @"\bdi toi\b|\bdi den\b|\bqua\b|\bsang\b", " den ");
            text = Regex.Replace(text, @"\btu\b", " tu ");
            text = Regex.Replace(text, @"\bden\b", " den ");

            // Viết tắt quận/huyện (cũ - giữ tương thích ngược)
            text = Regex.Replace(text, @"\bq\s*\.?\s*(\d{1,2})\b", "quan $1");
            text = Regex.Replace(text, @"\bquan\s*(\d{1,2})\b", "quan $1");

            // Thành phố
            text = Regex.Replace(text, @"\btp\s*\.?\s*hcm\b|\btphcm\b|\bho chi minh city\b", "ho chi minh");
            text = Regex.Replace(text, @"\bsg\b|\bsai gon\b", "ho chi minh");

            // Tên khu vực mới theo QĐ2913 - Bình Dương (sáp nhập)
            text = Regex.Replace(text, @"\bbinh duong\b|\bbd\b", "binh duong sap nhap");
            text = Regex.Replace(text, @"\bdi an\b", "di an binh duong sap nhap");
            text = Regex.Replace(text, @"\bthu dau mot\b", "thu dau mot binh duong sap nhap");
            text = Regex.Replace(text, @"\bthuan an\b", "thuan an binh duong sap nhap");
            text = Regex.Replace(text, @"\btan uyen\b", "tan uyen binh duong sap nhap");
            text = Regex.Replace(text, @"\bben cat\b", "ben cat binh duong sap nhap");
            text = Regex.Replace(text, @"\bbau bang\b", "bau bang binh duong sap nhap");
            text = Regex.Replace(text, @"\bdau tieng\b", "dau tieng binh duong sap nhap");
            text = Regex.Replace(text, @"\bphu giao\b", "phu giao binh duong sap nhap");

            // Tên khu vực mới theo QĐ2913 - Bà Rịa - Vũng Tàu (sáp nhập)
            text = Regex.Replace(text, @"\bba ria vung tau\b|\bbrvt\b|\bvung tau\b|\bba ria\b", "ba ria vung tau sap nhap");
            text = Regex.Replace(text, @"\bphu my\b", "phu my ba ria vung tau sap nhap");
            text = Regex.Replace(text, @"\bcon dao\b|\bdac khu con dao\b", "con dao ba ria vung tau sap nhap");
            text = Regex.Replace(text, @"\bxuyen moc\b", "xuyen moc ba ria vung tau sap nhap");
            text = Regex.Replace(text, @"\blong dien\b", "long dien ba ria vung tau sap nhap");
            text = Regex.Replace(text, @"\bdat do\b", "dat do ba ria vung tau sap nhap");
            text = Regex.Replace(text, @"\bchau duc\b", "chau duc ba ria vung tau sap nhap");

            // Tra cụm thi đua theo số
            text = Regex.Replace(text, @"\bcum\s*(\d{1,2})\b|\bcluster\s*(\d{1,2})\b", m =>
            {
                var num = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                return $"cum thi dua {num}";
            });

            text = Regex.Replace(text, @"[^a-z0-9\s]", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }

        public static string? CanonicalizeDistrict(string district)
        {
            if (string.IsNullOrWhiteSpace(district))
            {
                return null;
            }

            var normalized = NormalizeForIntent(district);

            // Quận cũ (số) → DistrictName mới theo QĐ2913
            var qMatch = Regex.Match(normalized, @"\bquan\s+(\d{1,2})\b");
            if (qMatch.Success && int.TryParse(qMatch.Groups[1].Value, out var qNum))
            {
                return qNum switch
                {
                    // DB hiện tại dùng DistrictName = "Cụm X" (seed cụm 1-9)
                    1 or 3 or 10 => "Cụm 1",
                    4 or 7 or 8  => "Cụm 2",
                    5 or 6 or 11 => "Cụm 3",
                    12           => "Cụm 7",
                    2 or 9       => "Cụm 8",
                    _            => null
                };
            }

            if (normalized.Contains("thu duc"))
            {
                return "Cụm 8";
            }

            if (normalized.Contains("binh duong"))
            {
                return "Cụm 9";
            }

            if (normalized.Contains("ba ria") || normalized.Contains("vung tau") || normalized.Contains("brvt"))
            {
                return "Bà Rịa - Vũng Tàu (sáp nhập)";
            }

            // Cluster-based lookup theo QĐ2913
            var clusterMatch = Regex.Match(normalized, @"\bcum\s*(?:thi\s*dua\s*)?(\d{1,2})\b");
            if (clusterMatch.Success)
            {
                return $"Cụm {clusterMatch.Groups[1].Value}";
            }

            // Quận/huyện cũ có tên → DistrictName mới theo QĐ2913
            var namedMap = new Dictionary<string, string>
            {
                ["tan binh"] = "Cụm 4",
                ["phu nhuan"] = "Cụm 4",
                ["binh tan"] = "Cụm 5",
                ["tan phu"] = "Cụm 5",
                ["binh thanh"] = "Cụm 6",
                ["go vap"] = "Cụm 6",
                ["quan 12"] = "Cụm 7",
                ["hoc mon"] = "Cụm 7",
            };
            foreach (var kv in namedMap)
                if (normalized.Contains(kv.Key))
                    return kv.Value;

            return ToTitleCaseVietnamese(district.Trim());
        }

        public static string CanonicalizeLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                return location;
            }

            var normalized = NormalizeForIntent(location);
            return ToTitleCaseVietnamese(normalized);
        }

        private static string RemoveDiacritics(string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalizedString.Length);
            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Replace('đ', 'd').Normalize(NormalizationForm.FormC);
        }

        private static string ToTitleCaseVietnamese(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return CultureInfo.GetCultureInfo("vi-VN").TextInfo.ToTitleCase(text.ToLowerInvariant());
        }
    }
}

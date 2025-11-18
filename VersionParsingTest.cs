using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace AssetStudio.Test
{
    class VersionParsingTest
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Unity Version Parsing Test ===\n");

            // Test various Unity versions
            TestVersion("2020.3.48f1");
            TestVersion("2021.3.2f1");
            TestVersion("2022.3.1f1");
            TestVersion("2023.2.20f1");
            TestVersion("6000.0.58f2");  // Unity 6
            TestVersion("6000.0.0f1");
            TestVersion("6001.1.2b1");   // Hypothetical future version

            Console.WriteLine("\n=== Version Comparison Tests ===\n");
            TestComparison("2022.3.1f1", "version[0] > 2022", false);
            TestComparison("2023.1.0f1", "version[0] > 2022", true);
            TestComparison("6000.0.58f2", "version[0] > 2022", true);
            TestComparison("6000.0.58f2", "version[0] >= 2021", true);
            TestComparison("6000.0.58f2", "version[0] > 2023", true);
            TestComparison("2020.1.0f1", "version[0] < 2020", false);
            TestComparison("2019.4.40f1", "version[0] < 2020", true);

            Console.WriteLine("\n=== Bundle File Encryption Flag Test ===\n");
            TestEncryptionFlag("2020.3.34f1", false);  // Should use old flag
            TestEncryptionFlag("2020.3.35f1", true);   // Should use new flag
            TestEncryptionFlag("2021.3.2f1", false);   // Should use old flag
            TestEncryptionFlag("2021.3.3f1", true);    // Should use new flag
            TestEncryptionFlag("2022.3.1f1", false);   // Should use old flag
            TestEncryptionFlag("2022.3.2f1", true);    // Should use new flag
            TestEncryptionFlag("2023.1.0f1", true);    // Should use new flag
            TestEncryptionFlag("6000.0.58f2", true);   // Unity 6 should use new flag

            Console.WriteLine("\n=== All Tests Completed ===");
        }

        static void TestVersion(string versionString)
        {
            Console.WriteLine($"Testing: {versionString}");

            // Simulate SetVersion logic
            var buildSplit = Regex.Replace(versionString, @"\d", "").Split(new[] { "." }, StringSplitOptions.RemoveEmptyEntries);
            var buildType = buildSplit.Length > 0 ? buildSplit[0] : "unknown";

            var versionSplit = Regex.Replace(versionString, @"\D", ".").Split(new[] { "." }, StringSplitOptions.RemoveEmptyEntries);
            var version = versionSplit.Select(int.Parse).ToArray();

            Console.WriteLine($"  Build Type: {buildType}");
            Console.WriteLine($"  Version Array: [{string.Join(", ", version)}]");
            Console.WriteLine($"  Major: {version[0]}, Minor: {version[1]}, Patch: {version[2]}");
            Console.WriteLine();
        }

        static void TestComparison(string versionString, string comparison, bool expectedResult)
        {
            var versionSplit = Regex.Replace(versionString, @"\D", ".").Split(new[] { "." }, StringSplitOptions.RemoveEmptyEntries);
            var version = versionSplit.Select(int.Parse).ToArray();

            bool result = false;
            if (comparison.Contains(">") && !comparison.Contains(">="))
            {
                var value = int.Parse(Regex.Match(comparison, @"\d+").Value);
                result = version[0] > value;
            }
            else if (comparison.Contains(">="))
            {
                var value = int.Parse(Regex.Match(comparison, @"\d+").Value);
                result = version[0] >= value;
            }
            else if (comparison.Contains("<") && !comparison.Contains("<="))
            {
                var value = int.Parse(Regex.Match(comparison, @"\d+").Value);
                result = version[0] < value;
            }

            var status = result == expectedResult ? "✓ PASS" : "✗ FAIL";
            Console.WriteLine($"{status}: {versionString} | {comparison} => {result} (expected: {expectedResult})");
        }

        static void TestEncryptionFlag(string versionString, bool expectedNewFlag)
        {
            var versionSplit = Regex.Replace(versionString, @"\D", ".").Split(new[] { "." }, StringSplitOptions.RemoveEmptyEntries);
            var version = versionSplit.Select(int.Parse).ToArray();

            // Simulate BundleFile encryption flag logic
            bool usesNewFlag;
            if (version[0] < 2020 ||
                (version[0] == 2020 && version[1] == 3 && version[2] <= 34) ||
                (version[0] == 2021 && version[1] == 3 && version[2] <= 2) ||
                (version[0] == 2022 && version[1] == 3 && version[2] <= 1))
            {
                usesNewFlag = false;  // BlockInfoNeedPaddingAtStart
            }
            else
            {
                usesNewFlag = true;   // UnityCNEncryption
            }

            var status = usesNewFlag == expectedNewFlag ? "✓ PASS" : "✗ FAIL";
            var flagName = usesNewFlag ? "UnityCNEncryption" : "BlockInfoNeedPaddingAtStart";
            Console.WriteLine($"{status}: {versionString} => {flagName} (expected: {(expectedNewFlag ? "new" : "old")} flag)");
        }
    }
}

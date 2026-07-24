#!/usr/bin/env python3

import unittest

from select_native_contracts import classify


class NativeContractSelectionTests(unittest.TestCase):
    def test_documentation_only_change_keeps_fast_source_lane(self) -> None:
        result = classify(["README.md", "docs/operator-guide.md"])

        self.assertFalse(result["installed"])
        self.assertEqual("source-only", result["contracts"])

    def test_hub_transport_change_selects_connection_and_installed_contracts(self) -> None:
        result = classify(["src/LabTetherAgent/Services/ConnectionTester.cs"])

        self.assertTrue(result["connection"])
        self.assertTrue(result["installed"])
        self.assertIn("hub-connection-security", result["contracts"])

    def test_manifest_change_selects_permissions_packaging_and_native_host(self) -> None:
        result = classify(["src/LabTetherAgent/Package.appxmanifest"])

        self.assertTrue(result["permissions"])
        self.assertTrue(result["packaging"])
        self.assertTrue(result["native_host"])

    def test_signing_script_selects_local_signing_boundary(self) -> None:
        result = classify(["scripts/release/sign-windows-release.sh"])

        self.assertTrue(result["signing"])
        self.assertTrue(result["installed"])
        self.assertIn("signing-boundary", result["contracts"])

    def test_workflow_change_selects_packaging_contract(self) -> None:
        result = classify(["./.github/workflows/ci.yml"])

        self.assertTrue(result["packaging"])
        self.assertTrue(result["installed"])

    def test_windows_ui_change_selects_installed_contract(self) -> None:
        result = classify(["src/LabTetherAgent/Views/Settings/SettingsWindow.xaml.cs"])

        self.assertTrue(result["platform"])
        self.assertTrue(result["installed"])

    def test_other_shipping_windows_code_still_selects_installed_contract(self) -> None:
        result = classify(["src/LabTetherAgent/Services/DiagnosticsCollector.cs"])

        self.assertTrue(result["platform"])
        self.assertTrue(result["installed"])

    def test_missing_git_history_fails_safe_to_every_contract(self) -> None:
        result = classify(["__all__"])

        for contract in ("connection", "permissions", "packaging", "signing", "platform", "installed"):
            self.assertTrue(result[contract])


if __name__ == "__main__":
    unittest.main()

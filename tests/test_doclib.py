import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parent.parent
DOCLIB = REPO_ROOT / "tools" / "doclib.py"


class DocLibCliTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.library = self.root / "docs"
        self.source = self.root / "source.md"
        self.source.write_text("# 测试资料\n\n正文内容。\n", encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def run_cli(self, *args, expected=0):
        result = subprocess.run(
            [sys.executable, str(DOCLIB), "--library", str(self.library), *args],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
        self.assertEqual(
            expected,
            result.returncode,
            msg=f"stdout:\n{result.stdout}\nstderr:\n{result.stderr}",
        )
        return result

    def add(self, *, status="draft", origin="human", source=None):
        source = source or self.source
        args = [
            "add",
            str(source),
            "--title",
            "测试资料",
            "--type",
            "design",
            "--domain",
            "project",
            "--status",
            status,
            "--owner",
            "测试负责人",
            "--summary",
            "用于验证资料库",
            "--tag",
            "test",
            "--origin",
            origin,
            "--json",
        ]
        if origin == "ai":
            args.extend(
                ["--generator", "Codex", "--model-version", "test-model"]
            )
        result = self.run_cli(*args)
        return json.loads(result.stdout)

    def meta_path(self, meta):
        return self.library / "entries" / meta["domain"] / meta["id"] / "meta.json"

    def read_meta(self, meta):
        return json.loads(self.meta_path(meta).read_text(encoding="utf-8"))

    def test_add_search_show_and_validate(self):
        meta = self.add()
        self.assertRegex(meta["id"], r"^DOC-\d{8}-001$")
        self.assertTrue(
            (self.library / "entries" / "project" / meta["id"] / "primary.md").is_file()
        )

        listed = json.loads(self.run_cli("list", "--json").stdout)
        self.assertEqual(meta["id"], listed[0]["id"])

        found = json.loads(
            self.run_cli("search", "正文", "--json").stdout
        )
        self.assertEqual(meta["id"], found[0]["id"])
        self.assertIn("正文内容", found[0]["snippet"])

        shown = json.loads(self.run_cli("show", meta["id"], "--json").stdout)
        self.assertEqual("测试资料", shown["title"])
        self.run_cli("validate")

    def test_duplicate_and_invalid_transition_are_rejected(self):
        meta = self.add()
        result = self.run_cli(
            "add",
            str(self.source),
            "--title",
            "重复资料",
            "--type",
            "report",
            "--domain",
            "project",
            "--owner",
            "测试负责人",
            expected=2,
        )
        self.assertIn("相同内容", result.stderr)

        result = self.run_cli(
            "status",
            meta["id"],
            "active",
            "--actor",
            "测试人",
            expected=2,
        )
        self.assertIn("非法状态流转", result.stderr)

    def test_ai_active_requires_review_note(self):
        meta = self.add(status="review", origin="ai")
        result = self.run_cli(
            "status",
            meta["id"],
            "active",
            "--actor",
            "核验人",
            expected=2,
        )
        self.assertIn("人工核验说明", result.stderr)

        self.run_cli(
            "status",
            meta["id"],
            "active",
            "--actor",
            "核验人",
            "--note",
            "已核对",
        )
        updated = self.read_meta(meta)
        self.assertEqual("reviewed", updated["provenance"]["humanReview"])
        self.run_cli("validate")

    def test_revise_preserves_old_content_and_returns_to_draft(self):
        meta = self.add(status="review")
        revised = self.root / "revised.md"
        revised.write_text("# 修订版\n\n新增内容。\n", encoding="utf-8")
        self.run_cli(
            "revise",
            meta["id"],
            str(revised),
            "--actor",
            "测试人",
            "--note",
            "补充内容",
        )
        updated = self.read_meta(meta)
        entry = self.meta_path(meta).parent
        self.assertEqual(2, updated["revision"])
        self.assertEqual("draft", updated["status"])
        self.assertEqual(1, len(updated["versions"]))
        old_path = entry / updated["versions"][0]["path"]
        self.assertIn("测试资料", old_path.read_text(encoding="utf-8"))
        self.assertIn(
            "修订版", (entry / updated["primaryFile"]).read_text(encoding="utf-8")
        )
        self.run_cli("validate")

        old_path.write_text("旧版本被篡改", encoding="utf-8")
        result = self.run_cli("validate", expected=1)
        self.assertIn("旧版本文件哈希不匹配", result.stdout)

    def test_supersede_links_both_directions(self):
        old = self.add(status="review")
        self.run_cli(
            "status", old["id"], "active", "--actor", "评审人", "--note", "通过"
        )

        new_source = self.root / "new.md"
        new_source.write_text("# 新资料\n\n替代旧资料。\n", encoding="utf-8")
        new = self.add(status="review", source=new_source)
        self.run_cli(
            "status", new["id"], "active", "--actor", "评审人", "--note", "通过"
        )
        self.run_cli(
            "supersede",
            old["id"],
            new["id"],
            "--actor",
            "测试人",
            "--note",
            "新版替代旧版",
        )
        old_updated = self.read_meta(old)
        new_updated = self.read_meta(new)
        self.assertEqual("superseded", old_updated["status"])
        self.assertEqual(new["id"], old_updated["supersededBy"])
        self.assertEqual(old["id"], new_updated["supersedes"])
        self.run_cli("validate")

    def test_tamper_and_stale_index_are_detected_then_rebuilt(self):
        meta = self.add()
        primary = self.meta_path(meta).parent / meta["primaryFile"]
        primary.write_text("未登记的修改", encoding="utf-8")
        result = self.run_cli("validate", expected=1)
        self.assertIn("哈希不匹配", result.stdout)

        self.run_cli("revise", meta["id"], str(primary), "--actor", "测试人", "--note", "忽略", expected=2)

        self.library.joinpath("INDEX.md").write_text("stale", encoding="utf-8")
        result = self.run_cli("validate", expected=1)
        self.assertIn("INDEX.md 已过期", result.stdout)
        self.run_cli("rebuild")
        result = self.run_cli("validate", expected=1)
        self.assertIn("哈希不匹配", result.stdout)

    def test_archived_documents_are_hidden_by_default(self):
        meta = self.add()
        self.run_cli(
            "status",
            meta["id"],
            "archived",
            "--actor",
            "测试人",
            "--note",
            "不再使用",
        )
        self.assertEqual([], json.loads(self.run_cli("list", "--json").stdout))
        self.assertEqual(
            [],
            json.loads(self.run_cli("search", "测试资料", "--json").stdout),
        )
        visible = json.loads(
            self.run_cli("list", "--include-archived", "--json").stdout
        )
        self.assertEqual(meta["id"], visible[0]["id"])
        self.run_cli("validate")


if __name__ == "__main__":
    unittest.main()

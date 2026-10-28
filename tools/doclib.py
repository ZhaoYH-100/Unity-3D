#!/usr/bin/env python3
"""Local document library manager for generated project documents.

The tool intentionally uses only the Python standard library so the library
works on a clean Windows development machine.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import mimetypes
import os
import re
import shutil
import sys
import tempfile
from collections import Counter
from datetime import datetime
from pathlib import Path
from typing import Any, Iterable


SCHEMA_VERSION = 1
MAX_SOURCE_BYTES = 100 * 1024 * 1024
MAX_SEARCH_TEXT_BYTES = 2 * 1024 * 1024

STATUS_ORDER = ["active", "review", "draft", "superseded", "archived"]
STATUS_LABELS = {
    "active": "现行",
    "review": "评审中",
    "draft": "草稿",
    "superseded": "已被替代",
    "archived": "已归档",
}
STATUS_TRANSITIONS = {
    "draft": {"review", "archived"},
    "review": {"draft", "active", "archived"},
    "active": {"archived"},
    "superseded": {"archived"},
    "archived": set(),
}

DOC_TYPES = {
    "requirements",
    "plan",
    "design",
    "specification",
    "report",
    "test-plan",
    "decision",
    "runbook",
    "meeting",
    "delivery",
    "research",
    "other",
}
DOMAINS = {
    "general",
    "project",
    "requirements",
    "unity",
    "backend",
    "web",
    "qa",
    "delivery",
    "research",
}
ORIGINS = {"human", "ai", "mixed", "imported"}
AI_ORIGINS = {"ai", "mixed"}
REVIEW_STATES = {"pending", "reviewed", "not-required"}

TEXT_SUFFIXES = {
    ".md",
    ".markdown",
    ".txt",
    ".json",
    ".yaml",
    ".yml",
    ".csv",
    ".toml",
    ".ini",
    ".log",
    ".xml",
    ".html",
    ".css",
    ".js",
    ".jsx",
    ".ts",
    ".tsx",
    ".cs",
    ".go",
    ".py",
    ".ps1",
    ".sql",
    ".sh",
}

ID_RE = re.compile(r"^DOC-(\d{8})-(\d{3})$")
SECRET_PATTERNS = [
    (
        "私钥文件头",
        re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
    ),
    (
        "疑似令牌或口令",
        re.compile(
            r"(?i)(?:api[_-]?key|access[_-]?token|secret|password)"
            r"\s*[:=]\s*['\"]?[A-Za-z0-9_./+\-]{12,}"
        ),
    ),
]


class DocLibError(Exception):
    """Expected command failure with a user-facing message."""


def now_iso() -> str:
    return datetime.now().astimezone().isoformat(timespec="seconds")


def today_compact() -> str:
    return datetime.now().astimezone().strftime("%Y%m%d")


def library_paths(root: Path) -> dict[str, Path]:
    root = root.resolve()
    return {
        "root": root,
        "entries": root / "entries",
        "catalog": root / "catalog.json",
        "index": root / "INDEX.md",
    }


def init_library(root: Path) -> None:
    paths = library_paths(root)
    for key in ("root", "entries"):
        paths[key].mkdir(parents=True, exist_ok=True)
    for domain in sorted(DOMAINS):
        (paths["entries"] / domain).mkdir(parents=True, exist_ok=True)
    rebuild(root, quiet=True)


def _atomic_write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    handle = tempfile.NamedTemporaryFile(
        mode="w",
        encoding="utf-8",
        newline="\n",
        delete=False,
        dir=path.parent,
        prefix=f".{path.name}.",
        suffix=".tmp",
    )
    temp_path = Path(handle.name)
    try:
        with handle:
            handle.write(content)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temp_path, path)
    except Exception:
        temp_path.unlink(missing_ok=True)
        raise


def _atomic_write_json(path: Path, payload: Any) -> None:
    content = json.dumps(
        payload,
        ensure_ascii=False,
        indent=2,
        sort_keys=True,
    )
    _atomic_write_text(path, content + "\n")


def _read_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise DocLibError(f"文件不存在：{path}") from exc
    except json.JSONDecodeError as exc:
        raise DocLibError(f"JSON 格式错误：{path}（{exc}）") from exc


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _file_info(entry_dir: Path, relative_path: str) -> dict[str, Any]:
    path = _safe_entry_path(entry_dir, relative_path)
    if not path.is_file():
        raise DocLibError(f"资料文件不存在：{path}")
    media_type = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
    return {
        "path": Path(relative_path).as_posix(),
        "sha256": _sha256(path),
        "sizeBytes": path.stat().st_size,
        "mediaType": media_type,
    }


def _safe_entry_path(entry_dir: Path, relative_path: str) -> Path:
    relative = Path(relative_path)
    if relative.is_absolute() or ".." in relative.parts:
        raise DocLibError(f"资料相对路径不安全：{relative_path}")
    resolved = (entry_dir / relative).resolve()
    try:
        resolved.relative_to(entry_dir.resolve())
    except ValueError as exc:
        raise DocLibError(f"资料路径越出条目目录：{relative_path}") from exc
    return resolved


def _iter_meta_paths(root: Path) -> Iterable[Path]:
    entries = library_paths(root)["entries"]
    if not entries.exists():
        return []
    return sorted(entries.glob("*/*/meta.json"))


def load_records(root: Path) -> list[tuple[Path, dict[str, Any]]]:
    records: list[tuple[Path, dict[str, Any]]] = []
    ids: set[str] = set()
    for meta_path in _iter_meta_paths(root):
        meta = _read_json(meta_path)
        if not isinstance(meta, dict):
            raise DocLibError(f"元数据必须是 JSON 对象：{meta_path}")
        doc_id = str(meta.get("id", ""))
        if doc_id in ids:
            raise DocLibError(f"重复资料 ID：{doc_id}")
        ids.add(doc_id)
        records.append((meta_path.parent, meta))
    records.sort(key=lambda item: str(item[1].get("id", "")))
    return records


def find_record(root: Path, doc_id: str) -> tuple[Path, dict[str, Any]]:
    normalized = doc_id.strip().upper()
    for entry_dir, meta in load_records(root):
        if str(meta.get("id", "")).upper() == normalized:
            return entry_dir, meta
    raise DocLibError(f"未找到资料：{doc_id}")


def _next_id(root: Path) -> str:
    prefix = f"DOC-{today_compact()}-"
    sequence = 0
    for entry_dir, meta in load_records(root):
        del entry_dir
        match = ID_RE.match(str(meta.get("id", "")))
        if match and str(meta["id"]).startswith(prefix):
            sequence = max(sequence, int(match.group(2)))
    return f"{prefix}{sequence + 1:03d}"


def _validate_enum(value: str, allowed: set[str], label: str) -> str:
    normalized = value.strip().lower()
    if normalized not in allowed:
        options = "、".join(sorted(allowed))
        raise DocLibError(f"{label} 无效：{value}；可选值：{options}")
    return normalized


def _normalize_tags(tags: Iterable[str]) -> list[str]:
    result: list[str] = []
    seen: set[str] = set()
    for raw in tags:
        for item in raw.split(","):
            tag = item.strip()
            key = tag.casefold()
            if tag and key not in seen:
                seen.add(key)
                result.append(tag)
    return result


def _require_nonempty(value: str | None, label: str) -> str:
    if value is None or not value.strip():
        raise DocLibError(f"{label}不能为空")
    return value.strip()


def _history_event(
    *,
    action: str,
    actor: str,
    note: str,
    from_status: str | None,
    to_status: str,
) -> dict[str, Any]:
    return {
        "at": now_iso(),
        "action": action,
        "actor": actor,
        "fromStatus": from_status,
        "toStatus": to_status,
        "note": note,
    }


def add_document(
    root: Path,
    source: Path,
    *,
    title: str,
    doc_type: str,
    domain: str,
    status: str,
    owner: str,
    version: str,
    summary: str,
    tags: Iterable[str],
    origin: str,
    generator: str | None,
    model_version: str | None,
    prompt_ref: str | None,
    source_refs: Iterable[str],
    related: Iterable[str],
    note: str,
    allow_duplicate: bool = False,
) -> tuple[Path, dict[str, Any]]:
    init_library(root)
    source = source.resolve()
    if not source.is_file():
        raise DocLibError(f"来源不是文件：{source}")
    size = source.stat().st_size
    if size > MAX_SOURCE_BYTES:
        raise DocLibError(
            f"来源文件超过 {MAX_SOURCE_BYTES // (1024 * 1024)} MiB 限制：{source}"
        )

    doc_type = _validate_enum(doc_type, DOC_TYPES, "资料类型")
    domain = _validate_enum(domain, DOMAINS, "资料域")
    status = _validate_enum(status, set(STATUS_ORDER), "资料状态")
    origin = _validate_enum(origin, ORIGINS, "来源类型")
    title = _require_nonempty(title, "标题")
    owner = _require_nonempty(owner, "负责人")
    version = _require_nonempty(version, "版本")
    if status not in {"draft", "review", "active"}:
        raise DocLibError("新资料只能以 draft、review 或 active 状态入库")
    if origin in AI_ORIGINS:
        generator = _require_nonempty(generator, "AI 生成工具")
        model_version = _require_nonempty(model_version, "AI 模型版本")
    if status == "active" and origin in AI_ORIGINS:
        raise DocLibError(
            "AI 生成资料不能直接以 active 入库；先以 review 入库并完成人工核验"
        )

    source_hash = _sha256(source)
    if not allow_duplicate:
        for existing_dir, existing_meta in load_records(root):
            del existing_dir
            existing_hash = (
                existing_meta.get("file", {}).get("sha256")
                if isinstance(existing_meta.get("file"), dict)
                else None
            )
            if existing_hash == source_hash:
                raise DocLibError(
                    f"相同内容的资料已存在：{existing_meta.get('id')}；"
                    "如确需重复入库，使用 --allow-duplicate"
                )

    doc_id = _next_id(root)
    entry_dir = library_paths(root)["entries"] / domain / doc_id
    for ref in related:
        find_record(root, ref)
    for ref in source_refs:
        _require_nonempty(ref, "来源引用")

    entry_dir.mkdir(parents=True, exist_ok=False)
    suffix = source.suffix.lower() or ".bin"
    primary_name = f"primary{suffix}"
    primary_path = entry_dir / primary_name
    temp_path = entry_dir / f".{primary_name}.tmp"
    try:
        shutil.copy2(source, temp_path)
        os.replace(temp_path, primary_path)
        timestamp = now_iso()
        file_info = _file_info(entry_dir, primary_name)
        meta: dict[str, Any] = {
            "schemaVersion": SCHEMA_VERSION,
            "id": doc_id,
            "title": title,
            "type": doc_type,
            "domain": domain,
            "status": status,
            "version": version,
            "revision": 1,
            "owner": owner,
            "createdAt": timestamp,
            "updatedAt": timestamp,
            "summary": summary.strip(),
            "tags": _normalize_tags(tags),
            "primaryFile": primary_name,
            "file": file_info,
            "provenance": {
                "origin": origin,
                "generator": (generator or "").strip(),
                "modelVersion": (model_version or "").strip(),
                "promptRef": (prompt_ref or "not-recorded").strip(),
                "sourceRefs": [
                    item.strip() for item in source_refs if item.strip()
                ],
                "humanReview": (
                    "not-required" if origin not in AI_ORIGINS else "pending"
                ),
                "reviewNotes": "",
            },
            "related": [item.upper() for item in related],
            "supersedes": None,
            "supersededBy": None,
            "versions": [],
            "history": [
                _history_event(
                    action="imported",
                    actor=owner,
                    note=note.strip() or "首次入库",
                    from_status=None,
                    to_status=status,
                )
            ],
        }
        _atomic_write_json(entry_dir / "meta.json", meta)
    except Exception:
        shutil.rmtree(entry_dir, ignore_errors=True)
        raise

    rebuild(root, quiet=True)
    return entry_dir, meta


def _filter_records(
    records: list[tuple[Path, dict[str, Any]]],
    *,
    status: str | None = None,
    doc_type: str | None = None,
    domain: str | None = None,
    tag: str | None = None,
    include_archived: bool = False,
) -> list[tuple[Path, dict[str, Any]]]:
    result: list[tuple[Path, dict[str, Any]]] = []
    tag_key = tag.casefold() if tag else None
    for entry_dir, meta in records:
        current_status = str(meta.get("status", ""))
        if status and current_status != status:
            continue
        if not status and not include_archived and current_status == "archived":
            continue
        if doc_type and meta.get("type") != doc_type:
            continue
        if domain and meta.get("domain") != domain:
            continue
        if tag_key:
            tags = {str(item).casefold() for item in meta.get("tags", [])}
            if tag_key not in tags:
                continue
        result.append((entry_dir, meta))
    result.sort(
        key=lambda item: (
            str(item[1].get("updatedAt", "")),
            str(item[1].get("id", "")),
        ),
        reverse=True,
    )
    return result


def list_documents(
    root: Path,
    *,
    status: str | None = None,
    doc_type: str | None = None,
    domain: str | None = None,
    tag: str | None = None,
    include_archived: bool = False,
    limit: int | None = None,
) -> list[dict[str, Any]]:
    records = _filter_records(
        load_records(root),
        status=status,
        doc_type=doc_type,
        domain=domain,
        tag=tag,
        include_archived=include_archived,
    )
    if limit is not None:
        records = records[: max(limit, 0)]
    return [
        {
            "id": meta["id"],
            "title": meta.get("title", ""),
            "status": meta.get("status", ""),
            "type": meta.get("type", ""),
            "domain": meta.get("domain", ""),
            "version": meta.get("version", ""),
            "updatedAt": meta.get("updatedAt", ""),
        }
        for _, meta in records
    ]


def _text_for_search(path: Path) -> str | None:
    if path.suffix.lower() not in TEXT_SUFFIXES:
        return None
    if path.stat().st_size > MAX_SEARCH_TEXT_BYTES:
        return None
    try:
        return path.read_text(encoding="utf-8-sig")
    except (UnicodeDecodeError, OSError):
        return None


def search_documents(
    root: Path,
    query: str,
    *,
    include_archived: bool = False,
    limit: int = 50,
) -> list[dict[str, Any]]:
    terms = [term.casefold() for term in query.split() if term.strip()]
    if not terms:
        raise DocLibError("检索词不能为空")

    matches: list[dict[str, Any]] = []
    for entry_dir, meta in _filter_records(
        load_records(root), include_archived=include_archived
    ):
        tags = " ".join(str(item) for item in meta.get("tags", []))
        metadata_text = " ".join(
            [
                str(meta.get("id", "")),
                str(meta.get("title", "")),
                str(meta.get("summary", "")),
                str(meta.get("owner", "")),
                str(meta.get("version", "")),
                tags,
            ]
        ).casefold()
        meta_hit = all(term in metadata_text for term in terms)

        content = ""
        primary = meta.get("primaryFile")
        if isinstance(primary, str):
            content = _text_for_search(_safe_entry_path(entry_dir, primary)) or ""
        content_folded = content.casefold()
        content_hit = bool(content) and all(term in content_folded for term in terms)
        if not meta_hit and not content_hit:
            continue

        snippet = str(meta.get("summary", "")).strip()
        if content_hit:
            position = min(
                (content_folded.find(term) for term in terms if term in content_folded),
                default=-1,
            )
            if position >= 0:
                start = max(position - 45, 0)
                end = min(position + 105, len(content))
                snippet = " ".join(content[start:end].split())
        matches.append(
            {
                "id": meta["id"],
                "title": meta.get("title", ""),
                "status": meta.get("status", ""),
                "matched": "metadata+content" if content_hit else "metadata",
                "snippet": snippet,
            }
        )
        if len(matches) >= limit:
            break
    return matches


def change_status(
    root: Path,
    doc_id: str,
    new_status: str,
    *,
    actor: str,
    note: str,
) -> dict[str, Any]:
    entry_dir, meta = find_record(root, doc_id)
    del entry_dir
    new_status = _validate_enum(new_status, set(STATUS_ORDER), "资料状态")
    old_status = str(meta.get("status", ""))
    if new_status == old_status:
        raise DocLibError(f"资料已处于 {new_status} 状态")
    if new_status not in STATUS_TRANSITIONS.get(old_status, set()):
        raise DocLibError(
            f"非法状态流转：{old_status} → {new_status}；"
            f"允许：{', '.join(sorted(STATUS_TRANSITIONS.get(old_status, set()))) or '无'}"
        )
    actor = _require_nonempty(actor, "操作人")
    note = note.strip()
    if (
        new_status == "active"
        and meta.get("provenance", {}).get("origin") in AI_ORIGINS
    ):
        if not note:
            raise DocLibError("AI 生成资料转入 active 前必须填写人工核验说明")
        meta["provenance"]["humanReview"] = "reviewed"
        meta["provenance"]["reviewNotes"] = f"{actor}：{note}"

    meta["history"].append(
        _history_event(
            action="status-changed",
            actor=actor,
            note=note or "状态流转",
            from_status=old_status,
            to_status=new_status,
        )
    )
    meta["status"] = new_status
    meta["updatedAt"] = now_iso()
    _atomic_write_json(find_record(root, doc_id)[0] / "meta.json", meta)
    rebuild(root, quiet=True)
    return meta


def revise_document(
    root: Path,
    doc_id: str,
    source: Path,
    *,
    actor: str,
    note: str,
    title: str | None = None,
    summary: str | None = None,
    origin: str | None = None,
    generator: str | None = None,
    model_version: str | None = None,
    prompt_ref: str | None = None,
) -> dict[str, Any]:
    entry_dir, meta = find_record(root, doc_id)
    old_status = str(meta.get("status", ""))
    if old_status in {"archived", "superseded"}:
        raise DocLibError(
            f"{old_status} 资料不得原位修订；请作为新资料入库并显式引用"
        )
    source = source.resolve()
    if not source.is_file():
        raise DocLibError(f"来源不是文件：{source}")
    if source.stat().st_size > MAX_SOURCE_BYTES:
        raise DocLibError("来源文件超过 100 MiB 限制")
    actor = _require_nonempty(actor, "操作人")
    note = _require_nonempty(note, "修订说明")

    old_primary_name = _require_nonempty(
        str(meta.get("primaryFile", "")), "当前资料文件"
    )
    old_primary = _safe_entry_path(entry_dir, old_primary_name)
    if not old_primary.is_file():
        raise DocLibError(f"当前资料文件不存在：{old_primary}")
    if source == old_primary.resolve():
        raise DocLibError("新版本来源不能是当前受管文件本身")

    current_revision = int(meta.get("revision", 1))
    versions_dir = entry_dir / "versions"
    versions_dir.mkdir(exist_ok=True)
    backup_name = f"v{current_revision:03d}_{old_primary.name}"
    backup_path = versions_dir / backup_name
    if backup_path.exists():
        raise DocLibError(f"旧版本快照已存在，停止覆盖：{backup_path}")

    new_origin = origin or str(meta.get("provenance", {}).get("origin", "human"))
    new_origin = _validate_enum(new_origin, ORIGINS, "来源类型")
    if new_origin in AI_ORIGINS:
        inherited = meta.get("provenance", {})
        generator = generator or inherited.get("generator")
        model_version = model_version or inherited.get("modelVersion")
        generator = _require_nonempty(generator, "AI 生成工具")
        model_version = _require_nonempty(model_version, "AI 模型版本")

    new_suffix = source.suffix.lower() or ".bin"
    new_primary_name = f"primary{new_suffix}"
    new_primary = entry_dir / new_primary_name
    if new_primary.exists() and new_primary != old_primary:
        raise DocLibError(f"目标主文件已存在，停止覆盖：{new_primary}")
    temp_path = entry_dir / f".{new_primary_name}.tmp"
    try:
        shutil.copy2(old_primary, backup_path)
        backup_info = _file_info(entry_dir, backup_path.relative_to(entry_dir).as_posix())
        shutil.copy2(source, temp_path)
        os.replace(temp_path, new_primary)
        if old_primary != new_primary:
            old_primary.unlink()
    except Exception:
        temp_path.unlink(missing_ok=True)
        raise

    new_info = _file_info(entry_dir, new_primary_name)
    versions = meta.setdefault("versions", [])
    versions.append(
        {
            "revision": current_revision,
            "path": backup_path.relative_to(entry_dir).as_posix(),
            "file": backup_info,
            "at": now_iso(),
            "actor": actor,
            "note": note,
        }
    )
    meta["revision"] = current_revision + 1
    meta["primaryFile"] = new_primary_name
    meta["file"] = new_info
    meta["status"] = "draft"
    meta["updatedAt"] = now_iso()
    if title is not None:
        meta["title"] = _require_nonempty(title, "标题")
    if summary is not None:
        meta["summary"] = summary.strip()
    provenance = meta.setdefault("provenance", {})
    provenance["origin"] = new_origin
    if generator is not None:
        provenance["generator"] = generator.strip()
    if model_version is not None:
        provenance["modelVersion"] = model_version.strip()
    if prompt_ref is not None:
        provenance["promptRef"] = prompt_ref.strip()
    provenance["humanReview"] = (
        "not-required" if new_origin not in AI_ORIGINS else "pending"
    )
    provenance["reviewNotes"] = ""
    meta["history"].append(
        _history_event(
            action="revised",
            actor=actor,
            note=note,
            from_status=old_status,
            to_status="draft",
        )
    )
    _atomic_write_json(entry_dir / "meta.json", meta)
    rebuild(root, quiet=True)
    return meta


def supersede_document(
    root: Path,
    old_id: str,
    new_id: str,
    *,
    actor: str,
    note: str,
) -> tuple[dict[str, Any], dict[str, Any]]:
    old_dir, old_meta = find_record(root, old_id)
    del old_dir
    new_dir, new_meta = find_record(root, new_id)
    del new_dir
    if old_meta["id"] == new_meta["id"]:
        raise DocLibError("不能用资料替代自身")
    if old_meta.get("status") != "active":
        raise DocLibError("只有 active 资料才能被替代")
    if new_meta.get("status") != "active":
        raise DocLibError("替代资料必须先处于 active 状态")
    if old_meta.get("supersededBy"):
        raise DocLibError(f"资料已由 {old_meta['supersededBy']} 替代")
    if new_meta.get("supersedes"):
        raise DocLibError(f"资料已经声明替代 {new_meta['supersedes']}")

    actor = _require_nonempty(actor, "操作人")
    note = _require_nonempty(note, "替代说明")
    timestamp = now_iso()
    old_meta["status"] = "superseded"
    old_meta["supersededBy"] = new_meta["id"]
    old_meta["updatedAt"] = timestamp
    old_meta["history"].append(
        _history_event(
            action="superseded",
            actor=actor,
            note=note,
            from_status="active",
            to_status="superseded",
        )
    )
    new_meta["supersedes"] = old_meta["id"]
    new_meta["updatedAt"] = timestamp
    new_meta["history"].append(
        {
            "at": timestamp,
            "action": "supersedes-linked",
            "actor": actor,
            "fromStatus": new_meta["status"],
            "toStatus": new_meta["status"],
            "note": note,
        }
    )
    _atomic_write_json(find_record(root, old_id)[0] / "meta.json", old_meta)
    _atomic_write_json(find_record(root, new_id)[0] / "meta.json", new_meta)
    rebuild(root, quiet=True)
    return old_meta, new_meta


def _summary_entry(root: Path, entry_dir: Path, meta: dict[str, Any]) -> dict[str, Any]:
    primary = str(meta.get("primaryFile", ""))
    path = ""
    if primary:
        try:
            path = (entry_dir / primary).relative_to(root).as_posix()
        except ValueError:
            path = ""
    return {
        "id": meta.get("id", ""),
        "title": meta.get("title", ""),
        "type": meta.get("type", ""),
        "domain": meta.get("domain", ""),
        "status": meta.get("status", ""),
        "version": meta.get("version", ""),
        "revision": meta.get("revision", 1),
        "owner": meta.get("owner", ""),
        "updatedAt": meta.get("updatedAt", ""),
        "summary": meta.get("summary", ""),
        "tags": meta.get("tags", []),
        "path": path,
        "supersedes": meta.get("supersedes"),
        "supersededBy": meta.get("supersededBy"),
    }


def build_catalog(
    root: Path, records: list[tuple[Path, dict[str, Any]]]
) -> dict[str, Any]:
    entries = [_summary_entry(root, entry_dir, meta) for entry_dir, meta in records]
    status_counts = Counter(str(item["status"]) for item in entries)
    domain_counts = Counter(str(item["domain"]) for item in entries)
    type_counts = Counter(str(item["type"]) for item in entries)
    latest = max(
        (str(item.get("updatedAt", "")) for item in entries),
        default="",
    )
    return {
        "schemaVersion": SCHEMA_VERSION,
        "entryCount": len(entries),
        "latestUpdate": latest,
        "byStatus": dict(sorted(status_counts.items())),
        "byDomain": dict(sorted(domain_counts.items())),
        "byType": dict(sorted(type_counts.items())),
        "entries": entries,
    }


def _escape_table(value: Any) -> str:
    return " ".join(str(value or "").split()).replace("|", "\\|")


def render_index(
    root: Path, records: list[tuple[Path, dict[str, Any]]]
) -> str:
    summaries = [_summary_entry(root, entry_dir, meta) for entry_dir, meta in records]
    status_counts = Counter(item["status"] for item in summaries)
    lines = [
        "# 资料库目录",
        "",
        "> 此文件由 `tools/doclib.py` 自动生成，请勿手工编辑；变更后运行 `rebuild`。",
        "",
        "## 概览",
        "",
        f"- 受管资料：{len(summaries)}",
    ]
    for status in STATUS_ORDER:
        lines.append(f"- {STATUS_LABELS[status]}（`{status}`）：{status_counts[status]}")

    for status in STATUS_ORDER:
        group = [item for item in summaries if item["status"] == status]
        lines.extend(["", f"## {STATUS_LABELS[status]}（{len(group)}）", ""])
        if not group:
            lines.append("暂无资料。")
            continue
        lines.extend(
            [
                "| ID | 标题 | 类型 | 域 | 版本 | 负责人 | 更新时间 | 摘要 |",
                "| --- | --- | --- | --- | --- | --- | --- | --- |",
            ]
        )
        for item in sorted(
            group,
            key=lambda value: (value["updatedAt"], value["id"]),
            reverse=True,
        ):
            title = _escape_table(item["title"])
            path = item["path"]
            linked_title = f"[{title}]({path})" if path else title
            lines.append(
                "| "
                + " | ".join(
                    [
                        _escape_table(item["id"]),
                        linked_title,
                        _escape_table(item["type"]),
                        _escape_table(item["domain"]),
                        _escape_table(item["version"]),
                        _escape_table(item["owner"]),
                        _escape_table(item["updatedAt"]),
                        _escape_table(item["summary"]),
                    ]
                )
                + " |"
            )
    lines.append("")
    return "\n".join(lines)


def rebuild(root: Path, *, quiet: bool = False) -> None:
    paths = library_paths(root)
    paths["entries"].mkdir(parents=True, exist_ok=True)
    records = load_records(root)
    catalog = build_catalog(paths["root"], records)
    index = render_index(paths["root"], records)
    _atomic_write_json(paths["catalog"], catalog)
    _atomic_write_text(paths["index"], index)
    if not quiet:
        print(f"已重建目录：{catalog['entryCount']} 份资料")


def _is_iso_datetime(value: Any) -> bool:
    if not isinstance(value, str) or not value:
        return False
    try:
        datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return False
    return True


def _secret_warnings(entry_dir: Path, meta: dict[str, Any]) -> list[str]:
    primary = meta.get("primaryFile")
    if not isinstance(primary, str):
        return []
    try:
        path = _safe_entry_path(entry_dir, primary)
    except DocLibError:
        return []
    content = _text_for_search(path)
    if content is None:
        return []
    findings: list[str] = []
    for label, pattern in SECRET_PATTERNS:
        if pattern.search(content):
            findings.append(f"{meta.get('id')}：正文疑似包含{label}，请人工检查")
    return findings


def validate_library(
    root: Path, *, strict: bool = False
) -> dict[str, list[str]]:
    paths = library_paths(root)
    errors: list[str] = []
    warnings: list[str] = []
    records: list[tuple[Path, dict[str, Any]]] = []
    try:
        records = load_records(root)
    except DocLibError as exc:
        return {"errors": [str(exc)], "warnings": []}

    all_ids = {str(meta.get("id", "")) for _, meta in records}
    for entry_dir, meta in records:
        label = str(meta.get("id", entry_dir.name))
        if meta.get("schemaVersion") != SCHEMA_VERSION:
            errors.append(f"{label}：schemaVersion 必须为 {SCHEMA_VERSION}")
        match = ID_RE.match(str(meta.get("id", "")))
        if not match:
            errors.append(f"{label}：ID 必须匹配 DOC-YYYYMMDD-NNN")
        if entry_dir.name != meta.get("id"):
            errors.append(f"{label}：条目目录名与 ID 不一致")
        if entry_dir.parent.name != meta.get("domain"):
            errors.append(f"{label}：所在资料域目录与元数据不一致")
        if meta.get("type") not in DOC_TYPES:
            errors.append(f"{label}：type 无效")
        if meta.get("domain") not in DOMAINS:
            errors.append(f"{label}：domain 无效")
        if meta.get("status") not in STATUS_ORDER:
            errors.append(f"{label}：status 无效")
        if not str(meta.get("title", "")).strip():
            errors.append(f"{label}：title 不能为空")
        if not str(meta.get("owner", "")).strip():
            errors.append(f"{label}：owner 不能为空")
        if not str(meta.get("version", "")).strip():
            errors.append(f"{label}：version 不能为空")
        if not isinstance(meta.get("revision"), int) or meta.get("revision", 0) < 1:
            errors.append(f"{label}：revision 必须是大于等于 1 的整数")
        if not isinstance(meta.get("tags", []), list):
            errors.append(f"{label}：tags 必须是数组")
        if not _is_iso_datetime(meta.get("createdAt")):
            errors.append(f"{label}：createdAt 不是 ISO 8601 时间")
        if not _is_iso_datetime(meta.get("updatedAt")):
            errors.append(f"{label}：updatedAt 不是 ISO 8601 时间")

        primary = meta.get("primaryFile")
        file_info = meta.get("file")
        if not isinstance(primary, str) or not primary:
            errors.append(f"{label}：primaryFile 不能为空")
        elif not isinstance(file_info, dict):
            errors.append(f"{label}：file 必须是对象")
        else:
            try:
                path = _safe_entry_path(entry_dir, primary)
                if not path.is_file():
                    errors.append(f"{label}：主文件不存在：{primary}")
                else:
                    if file_info.get("path") != Path(primary).as_posix():
                        errors.append(f"{label}：file.path 与 primaryFile 不一致")
                    if file_info.get("sha256") != _sha256(path):
                        errors.append(f"{label}：主文件哈希不匹配，可能被未登记修改")
                    if file_info.get("sizeBytes") != path.stat().st_size:
                        errors.append(f"{label}：主文件大小与元数据不一致")
            except DocLibError as exc:
                errors.append(f"{label}：{exc}")

        versions = meta.get("versions")
        if not isinstance(versions, list):
            errors.append(f"{label}：versions 必须是数组")
        else:
            current_revision = meta.get("revision")
            stored_revisions: list[int] = []
            for index, version in enumerate(versions, start=1):
                version_label = f"{label}.versions[{index}]"
                if not isinstance(version, dict):
                    errors.append(f"{version_label}：必须是对象")
                    continue
                version_revision = version.get("revision")
                if not isinstance(version_revision, int) or version_revision < 1:
                    errors.append(f"{version_label}：revision 必须是正整数")
                elif isinstance(current_revision, int) and version_revision >= current_revision:
                    errors.append(f"{version_label}：revision 必须小于当前修订号")
                else:
                    stored_revisions.append(version_revision)
                version_path = version.get("path")
                version_file = version.get("file")
                if not isinstance(version_path, str) or not version_path:
                    errors.append(f"{version_label}：path 不能为空")
                elif not Path(version_path).parts or Path(version_path).parts[0] != "versions":
                    errors.append(f"{version_label}：旧版本必须位于 versions/ 目录")
                elif not isinstance(version_file, dict):
                    errors.append(f"{version_label}：file 必须是对象")
                else:
                    try:
                        path = _safe_entry_path(entry_dir, version_path)
                        if not path.is_file():
                            errors.append(
                                f"{version_label}：旧版本文件不存在：{version_path}"
                            )
                        else:
                            if version_file.get("path") != Path(version_path).as_posix():
                                errors.append(
                                    f"{version_label}：file.path 与 path 不一致"
                                )
                            if version_file.get("sha256") != _sha256(path):
                                errors.append(f"{version_label}：旧版本文件哈希不匹配")
                            if version_file.get("sizeBytes") != path.stat().st_size:
                                errors.append(
                                    f"{version_label}：旧版本文件大小不一致"
                                )
                    except DocLibError as exc:
                        errors.append(f"{version_label}：{exc}")
            if isinstance(current_revision, int):
                expected_revisions = list(range(1, current_revision))
                if sorted(stored_revisions) != expected_revisions:
                    errors.append(
                        f"{label}：versions 修订序列必须完整覆盖 "
                        f"1..{current_revision - 1}"
                    )

        provenance = meta.get("provenance")
        if not isinstance(provenance, dict):
            errors.append(f"{label}：provenance 必须是对象")
        else:
            origin = provenance.get("origin")
            review = provenance.get("humanReview")
            if origin not in ORIGINS:
                errors.append(f"{label}：provenance.origin 无效")
            if review not in REVIEW_STATES:
                errors.append(f"{label}：provenance.humanReview 无效")
            if origin in AI_ORIGINS:
                if not str(provenance.get("generator", "")).strip():
                    errors.append(f"{label}：AI 资料必须记录 generator")
                if not str(provenance.get("modelVersion", "")).strip():
                    errors.append(f"{label}：AI 资料必须记录 modelVersion")
                if meta.get("status") == "active" and review != "reviewed":
                    errors.append(f"{label}：AI 资料为 active 时必须完成人工核验")
                if review == "reviewed" and not str(
                    provenance.get("reviewNotes", "")
                ).strip():
                    errors.append(f"{label}：已核验 AI 资料必须记录 reviewNotes")
            source_refs = provenance.get("sourceRefs")
            if not isinstance(source_refs, list):
                errors.append(f"{label}：provenance.sourceRefs 必须是数组")
            elif any(
                not isinstance(ref, str) or not ref.strip() for ref in source_refs
            ):
                errors.append(f"{label}：provenance.sourceRefs 只能包含非空字符串")

        related = meta.get("related", [])
        if not isinstance(related, list):
            errors.append(f"{label}：related 必须是数组")
        else:
            for ref in related:
                if not isinstance(ref, str) or not ref:
                    errors.append(f"{label}：related 只能包含非空资料 ID")
                elif ref == label:
                    errors.append(f"{label}：related 不得引用自身")
                elif ref not in all_ids:
                    errors.append(f"{label}：related 引用了不存在的 {ref}")

        supersedes = meta.get("supersedes")
        superseded_by = meta.get("supersededBy")
        if supersedes and supersedes not in all_ids:
            errors.append(f"{label}：supersedes 引用了不存在的 {supersedes}")
        if superseded_by and superseded_by not in all_ids:
            errors.append(f"{label}：supersededBy 引用了不存在的 {superseded_by}")
        if meta.get("status") == "superseded" and not superseded_by:
            errors.append(f"{label}：superseded 资料必须填写 supersededBy")

        history = meta.get("history")
        if not isinstance(history, list) or not history:
            errors.append(f"{label}：history 必须是非空数组")
        elif not all(isinstance(event, dict) for event in history):
            errors.append(f"{label}：history 每项都必须是对象")
        else:
            previous_status: str | None = None
            for index, event in enumerate(history, start=1):
                event_label = f"{label}.history[{index}]"
                from_status = event.get("fromStatus")
                to_status = event.get("toStatus")
                action = event.get("action")
                if index == 1 and from_status is not None:
                    errors.append(f"{event_label}：首个历史事件的 fromStatus 必须为 null")
                if index > 1 and from_status != previous_status:
                    errors.append(f"{event_label}：fromStatus 与上一事件 toStatus 不连续")
                if to_status not in STATUS_ORDER:
                    errors.append(f"{event_label}：toStatus 无效")
                if from_status is not None and from_status not in STATUS_ORDER:
                    errors.append(f"{event_label}：fromStatus 无效")
                if (
                    from_status in STATUS_ORDER
                    and to_status in STATUS_ORDER
                    and from_status != to_status
                    and to_status not in STATUS_TRANSITIONS[from_status]
                    and not (
                        action == "superseded"
                        and from_status == "active"
                        and to_status == "superseded"
                    )
                ):
                    errors.append(f"{event_label}：记录了非法状态流转")
                previous_status = to_status
            if previous_status != meta.get("status"):
                errors.append(f"{label}：history 最后状态与当前 status 不一致")

        warnings.extend(_secret_warnings(entry_dir, meta))

    for _, meta in records:
        for ref in {meta.get("supersedes"), meta.get("supersededBy")}:
            if not ref:
                continue
            try:
                _, other = find_record(root, str(ref))
            except DocLibError:
                continue
            if meta.get("supersedes") == ref and other.get("supersededBy") != meta["id"]:
                errors.append(
                    f"{meta['id']}：supersedes 与 {ref}.supersededBy 不是双向关联"
                )
            if meta.get("supersededBy") == ref and other.get("supersedes") != meta["id"]:
                errors.append(
                    f"{meta['id']}：supersededBy 与 {ref}.supersedes 不是双向关联"
                )

    expected_catalog = build_catalog(paths["root"], records)
    if not paths["catalog"].is_file():
        errors.append("catalog.json 不存在")
    else:
        try:
            actual_catalog = _read_json(paths["catalog"])
            if actual_catalog != expected_catalog:
                errors.append("catalog.json 已过期，请运行 rebuild")
        except DocLibError as exc:
            errors.append(str(exc))

    expected_index = render_index(paths["root"], records)
    if not paths["index"].is_file():
        errors.append("INDEX.md 不存在")
    else:
        actual_index = paths["index"].read_text(encoding="utf-8").replace("\r\n", "\n")
        if actual_index != expected_index:
            errors.append("INDEX.md 已过期，请运行 rebuild")

    if strict:
        errors.extend(warnings)
        warnings = []
    return {"errors": errors, "warnings": warnings}


def print_table(rows: list[dict[str, Any]]) -> None:
    if not rows:
        print("没有匹配的资料。")
        return
    print("ID\t状态\t类型\t资料域\t更新时间\t标题")
    for item in rows:
        print(
            "\t".join(
                [
                    str(item.get("id", "")),
                    str(item.get("status", "")),
                    str(item.get("type", "")),
                    str(item.get("domain", "")),
                    str(item.get("updatedAt", "")),
                    str(item.get("title", "")),
                ]
            )
        )


def _build_parser(default_library: Path) -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="资料库管理命令行：入库、检索、状态流转、版本替代与完整性校验"
    )
    parser.add_argument(
        "--library",
        type=Path,
        default=default_library,
        help=f"资料库根目录（默认：{default_library}）",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    subparsers.add_parser("init", help="初始化资料库并生成空目录")
    subparsers.add_parser("rebuild", help="根据元数据重建 catalog.json 与 INDEX.md")

    add_parser = subparsers.add_parser("add", help="导入并登记一份资料")
    add_parser.add_argument("source", type=Path)
    add_parser.add_argument("--title", required=True)
    add_parser.add_argument("--type", required=True, choices=sorted(DOC_TYPES))
    add_parser.add_argument("--domain", required=True, choices=sorted(DOMAINS))
    add_parser.add_argument(
        "--status", default="draft", choices=["draft", "review", "active"]
    )
    add_parser.add_argument("--owner", required=True)
    add_parser.add_argument("--version", default="0.1")
    add_parser.add_argument("--summary", default="")
    add_parser.add_argument("--tag", action="append", default=[])
    add_parser.add_argument("--origin", default="human", choices=sorted(ORIGINS))
    add_parser.add_argument("--generator")
    add_parser.add_argument("--model-version")
    add_parser.add_argument("--prompt-ref")
    add_parser.add_argument("--source-ref", action="append", default=[])
    add_parser.add_argument("--related", action="append", default=[])
    add_parser.add_argument("--note", default="")
    add_parser.add_argument("--allow-duplicate", action="store_true")
    add_parser.add_argument("--json", action="store_true")

    list_parser = subparsers.add_parser("list", help="按条件列出资料")
    list_parser.add_argument("--status", choices=STATUS_ORDER)
    list_parser.add_argument("--type", choices=sorted(DOC_TYPES))
    list_parser.add_argument("--domain", choices=sorted(DOMAINS))
    list_parser.add_argument("--tag")
    list_parser.add_argument("--include-archived", action="store_true")
    list_parser.add_argument("--limit", type=int)
    list_parser.add_argument("--json", action="store_true")

    search_parser = subparsers.add_parser("search", help="检索元数据与 UTF-8 文本正文")
    search_parser.add_argument("query")
    search_parser.add_argument("--include-archived", action="store_true")
    search_parser.add_argument("--limit", type=int, default=50)
    search_parser.add_argument("--json", action="store_true")

    show_parser = subparsers.add_parser("show", help="显示一份资料的元数据")
    show_parser.add_argument("id")
    show_parser.add_argument("--json", action="store_true")

    status_parser = subparsers.add_parser("status", help="执行受控状态流转")
    status_parser.add_argument("id")
    status_parser.add_argument("new_status", choices=STATUS_ORDER)
    status_parser.add_argument("--actor", default=os.environ.get("USERNAME", ""))
    status_parser.add_argument("--note", default="")
    status_parser.add_argument("--json", action="store_true")

    revise_parser = subparsers.add_parser(
        "revise", help="以新文件修订草稿/评审/现行资料，旧版自动留存"
    )
    revise_parser.add_argument("id")
    revise_parser.add_argument("source", type=Path)
    revise_parser.add_argument("--actor", default=os.environ.get("USERNAME", ""))
    revise_parser.add_argument("--note", required=True)
    revise_parser.add_argument("--title")
    revise_parser.add_argument("--summary")
    revise_parser.add_argument("--origin", choices=sorted(ORIGINS))
    revise_parser.add_argument("--generator")
    revise_parser.add_argument("--model-version")
    revise_parser.add_argument("--prompt-ref")
    revise_parser.add_argument("--json", action="store_true")

    supersede_parser = subparsers.add_parser(
        "supersede", help="把一份 active 资料标记为被另一份 active 资料替代"
    )
    supersede_parser.add_argument("old_id")
    supersede_parser.add_argument("new_id")
    supersede_parser.add_argument("--actor", default=os.environ.get("USERNAME", ""))
    supersede_parser.add_argument("--note", required=True)
    supersede_parser.add_argument("--json", action="store_true")

    validate_parser = subparsers.add_parser("validate", help="校验契约、哈希、关联和索引")
    validate_parser.add_argument("--strict", action="store_true")
    validate_parser.add_argument("--json", action="store_true")

    return parser


def _print_json(payload: Any) -> None:
    print(json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True))


def main(argv: list[str] | None = None) -> int:
    repo_root = Path(__file__).resolve().parent.parent
    parser = _build_parser(repo_root / "docs")
    args = parser.parse_args(argv)
    library = args.library.resolve()

    try:
        if args.command == "init":
            init_library(library)
            print(f"资料库已初始化：{library}")
            return 0
        if args.command == "rebuild":
            rebuild(library)
            return 0
        if args.command == "add":
            entry_dir, meta = add_document(
                library,
                args.source,
                title=args.title,
                doc_type=args.type,
                domain=args.domain,
                status=args.status,
                owner=args.owner,
                version=args.version,
                summary=args.summary,
                tags=args.tag,
                origin=args.origin,
                generator=args.generator,
                model_version=args.model_version,
                prompt_ref=args.prompt_ref,
                source_refs=args.source_ref,
                related=args.related,
                note=args.note,
                allow_duplicate=args.allow_duplicate,
            )
            if args.json:
                _print_json(meta)
            else:
                print(f"已入库：{meta['id']}  {entry_dir / meta['primaryFile']}")
            return 0
        if args.command == "list":
            rows = list_documents(
                library,
                status=args.status,
                doc_type=args.type,
                domain=args.domain,
                tag=args.tag,
                include_archived=args.include_archived,
                limit=args.limit,
            )
            if args.json:
                _print_json(rows)
            else:
                print_table(rows)
            return 0
        if args.command == "search":
            rows = search_documents(
                library,
                args.query,
                include_archived=args.include_archived,
                limit=args.limit,
            )
            if args.json:
                _print_json(rows)
            else:
                if not rows:
                    print("没有匹配的资料。")
                for item in rows:
                    print(f"{item['id']}\t{item['status']}\t{item['title']}")
                    if item["snippet"]:
                        print(f"  {item['snippet']}")
            return 0
        if args.command == "show":
            entry_dir, meta = find_record(library, args.id)
            payload = dict(meta)
            payload["_entryDir"] = str(entry_dir)
            if args.json:
                _print_json(payload)
            else:
                print(f"{meta['id']}  {meta['title']}")
                print(f"状态：{meta['status']}  版本：{meta['version']}  修订：{meta.get('revision', 1)}")
                print(f"类型：{meta['type']}  域：{meta['domain']}  负责人：{meta['owner']}")
                print(f"文件：{entry_dir / meta['primaryFile']}")
                print(f"摘要：{meta.get('summary', '')}")
                print(f"标签：{', '.join(meta.get('tags', [])) or '-'}")
                print(f"来源：{meta.get('provenance', {}).get('origin', '-')}")
            return 0
        if args.command == "status":
            meta = change_status(
                library,
                args.id,
                args.new_status,
                actor=args.actor,
                note=args.note,
            )
            if args.json:
                _print_json(meta)
            else:
                print(
                    f"状态已更新：{meta['id']} → {meta['status']}（v{meta['version']}）"
                )
            return 0
        if args.command == "revise":
            meta = revise_document(
                library,
                args.id,
                args.source,
                actor=args.actor,
                note=args.note,
                title=args.title,
                summary=args.summary,
                origin=args.origin,
                generator=args.generator,
                model_version=args.model_version,
                prompt_ref=args.prompt_ref,
            )
            if args.json:
                _print_json(meta)
            else:
                print(
                    f"已修订：{meta['id']}  修订号 {meta['revision']}  "
                    f"状态 {meta['status']}"
                )
            return 0
        if args.command == "supersede":
            old_meta, new_meta = supersede_document(
                library,
                args.old_id,
                args.new_id,
                actor=args.actor,
                note=args.note,
            )
            if args.json:
                _print_json({"old": old_meta, "new": new_meta})
            else:
                print(
                    f"替代关系已建立：{old_meta['id']} → {new_meta['id']}"
                )
            return 0
        if args.command == "validate":
            result = validate_library(library, strict=args.strict)
            if args.json:
                _print_json(result)
            else:
                for warning in result["warnings"]:
                    print(f"警告：{warning}")
                for error in result["errors"]:
                    print(f"错误：{error}")
                if result["errors"]:
                    print(
                        f"校验失败：{len(result['errors'])} 个错误，"
                        f"{len(result['warnings'])} 个警告"
                    )
                else:
                    print(
                        f"校验通过：0 个错误，{len(result['warnings'])} 个警告"
                    )
            return 1 if result["errors"] else 0
        parser.error("未知命令")
        return 2
    except DocLibError as exc:
        print(f"错误：{exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8")
    raise SystemExit(main())

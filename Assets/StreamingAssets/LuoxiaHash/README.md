# LuoxiaHash

Opaque **Deployment export**: local `content_hash` → relative path index under this folder.
Host never hardcodes pack story; Unity resolves sprites **only** by content hash from SessionView / render nodes.

## Replace when content changes

From Luoxia-Deployment (serves whatever pack provision uses):

```bash
npm run export:unity-hash
```

Do not hand-author identity; hash is the only key. Empty index is legal (all resolves miss with explicit errors).

## hash-index.json

```json
{
  "schema_version": 1,
  "entries": {
    "<64-char-sha256-hex>": "files/<hash>.png"
  }
}
```

- Key：引擎合同 `AssetContentRef.content_hash`（小写 hex SHA-256）
- Value：相对本目录的文件路径（正斜杠）
- Optional `entry_list` / `asset_id` fields are operator metadata only — Host must not branch on them
- 未命中：UI 显示显式错误标记，不使用假图冒充成功

菜单：`Luoxia/Assets/Ensure Hash Index Scaffold`

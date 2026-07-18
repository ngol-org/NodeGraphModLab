# バージョン管理方針

Node Graph Mod Lab (NGOL) は [Semantic Versioning](https://semver.org/) に準拠しつつ、
成果物を3つの独立した軸で管理する。

## 3つのバージョン軸

| 軸 | 対象ファイル | 誰のためか | 上げるタイミング |
|---|---|---|---|
| ① **Product Version** | `NodeGraphModLab.Core/NodeGraphModLab.Core.csproj`（Core）<br/>`WebUI/package.json`（WebUI） | エンドユーザー（配布zipを使う人） | 機能追加・不具合修正のたび |
| ② **NodeAPI Version** | `NodeGraphModLab.NodeAPI/NodeGraphModLab.NodeAPI.csproj`<br/>`NodeGraphModLab.BuiltinNodes/NodeGraphModLab.BuiltinNodes.csproj` | カスタムノード開発者（`.cs` を書く人） | `INode`/`IExecutionContext`/属性等、ノード開発者が触れる公開APIが変わった時のみ |
| ③ **MCP Version** | `mcp/package.json` | AIエージェント/MCPクライアント開発者 | MCPツール名・引数スキーマが変わった時、またはドキュメント体系・利用導線が大きく変わった時 |

**①の2ファイルは常に同じ値に揃える**（同一zipで常に同時配布される成果物のため）。
②・③は①とは独立にカウントする（①がMINORで大きく進んでも、対応するAPIに変更が無ければ②・③は上げない）。

## beta期間中（0.x系）のルール

全軸が現在 `0.x.y`。

- **PATCH (`0.x.Y`)**: 後方互換な不具合修正のみ。
- **MINOR (`0.X.0`)**: 新機能追加。**0.x系である間は破壊的変更を含んでよい**が、含む場合は
  `CHANGELOG.md` に該当バージョンの `### Breaking` 見出しで明記する。
- **`1.0.0`**: 将来、API/グラフスキーマの安定を宣言するタイミング（未定・将来課題）。

## 既存の別軸（本方針の対象外・変更なし）

以下は上記3軸とは別の互換性ゲートとして既に運用されており、本方針では変更しない。

- **Graph `schemaVersion`**（`NodeGraphModLab.NodeAPI/GraphDefinition.cs`）: 保存グラフJSON自体のフォーマット互換用。
- **WebUI Plugin `apiVersion`(int)**（`NodeGraphModLab.Core/Server/WebUiPluginManifest.cs`）: 外部WebUI `.js` プラグインのAPI互換ゲート。

## リリース時の手順

1. 変更が①〜③のどれに該当するか判定し、PATCH/MINORを決める
2. 破壊的変更を含む場合は `CHANGELOG.md` に `### Breaking` を明記
3. 該当ファイルのバージョンを更新（①は2ファイルとも同値に）
4. `dotnet build` / `npm run build` でビルド確認
5. `scripts/create-core-release-package.ps1` で配布zip生成
6. `git tag`

カスタムノード開発者向けの互換性の見方は `docs/node-developer-guide.md` の「バージョン互換性」節を参照。

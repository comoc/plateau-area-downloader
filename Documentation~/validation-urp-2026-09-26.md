# Issue #3: URP 実景検証記録

検証日: 2026-09-26

環境: Unity 6000.3.10f1、Universal 3D / URP 17.3.0、PLATEAU SDK for Unity 4.3.0。HDRP は導入していない。

## 実景データと結果

東京タワー周辺を次の範囲で取得し、港区の2025年度データ仕様 5.0を使用した。

| 項目 | 結果 |
| --- | --- |
| 範囲 | west 139.744 / east 139.746 / south 35.658 / north 35.660 |
| データ | bldg 2、tran 2、dem 1の計5 GML |
| 容量・展開 | ZIP 86.7 MiB、展開後 739.9 MiB、6,907ファイル |
| 展開後のGML | `53393589_tran_6697_op.gml`、`53393599_tran_6697_op.gml`、`53393589_bldg_6697_op.gml`、`53393599_bldg_6697_op.gml`、`533935_dem_6697_op.gml` |
| 公式SDKでのシーン生成 | 1 city、5 GML、11,713 Renderer / MeshFilter、テクスチャ付きMaterial 15,085 |
| 欠落・シェーダー | Missing Script 0、Missing Material 0、Error Shader 0 |

シーンは保存した。初回確認とUnity再起動後のシーン表示確認で Game / Scene / Hierarchy / Inspector を確認し、Console Error は各確認時0件だった。シーン検証ログ上、Missing Script・Missing Material・Error Shader は0件だった。公式SDKのUXMLエラーは下記の未解決事項として別記する。

実景シーン検証に使ったパッケージはコミット `b53d8572f4f0919e6997fd7aef7dc925152b33fb` 時点のもの。この後、パッケージはダウンロードと公式SDKへのフォルダー受け渡しを行う構成に変更され、独自Import処理は削除された。

## 現行パッケージの確認

変更後のパッケージを検証プロジェクトから `file:` 参照して EditMode テストを実行し、18/18 passed（失敗 0）を確認した。内訳は本パッケージの Editor テスト 17件と、SDK の Addressables サンプルテスト 1件。GUIでは保存済みデータの都市別フォルダー表示、公式SDKの起動、ローカル参照からフォルダーを受理するところまで確認した。

SDKの検証用 tarball `PLATEAU-SDK-for-Unity-v4.3.0.0.tgz` の SHA-256: `68a646674658f27af646ea36d2f640ffe29915b3963a50dfada18dc0cb5e83e0`。

## 検証範囲と未確認事項

- MacでのGUI確認。Windowsでは未検証。
- 合成スクロール1回で1段階移動することを画面上で確認した。Macトラックパッドの実機操作感は未検証。
- 公式SDKのUXMLで `RoadNetworkEditMode` が未定義となるエラーは Issue #2 の未解決事項。
- EditMode結果とシーン検証ログは作業環境内の一時ファイルから転記したもので、この文書にはログや画面キャプチャを添付していない。

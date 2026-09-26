# Issue #3: URP 実景検証記録

検証日: 2026-09-26

環境: macOS、Unity `6000.3.10f1`、新規 Universal 3D / URP `17.3.0`、PLATEAU SDK for Unity `4.3.0`。HDRP は導入していない。既存の HDRP プロジェクトは変更していない。Windows 実機は未検証。

## 対象と取得内容

`b33ab79023890c6e3956314af9c709aa0660a519` を基点とする公開候補を検証プロジェクトから `file:` 参照した。検証中に見つかった、空の検索結果を地図へ再描画する際の例外はソースで修正した。最終的な配布 SHA は公開前に確定する。

東京タワー周辺を西端経度 `139.744`、東端経度 `139.746`、南端緯度 `35.658`、北端緯度 `35.660` の範囲で検索し、港区 2025 年度・仕様 5.0 の建築物、道路、地形を取得した。ZIP `86.7 MiB`、展開後 `739.9 MiB`、`6,907` ファイル。GML は建築物 2、道路 2、地形 1 の計 5 ファイル。地理院タイルと CityGML の出典・条件は [第三者サービスとデータ](../THIRD_PARTY_NOTICES.md)に記載した。

都市別フォルダの `dataset/13103_minato-ku_pref_2025_citygml_1_op` を本ツールに表示し、公式 SDK のローカル参照で `udx` を含むフォルダとして受理された。パッケージ自身はインポート設定や実行を行わない。

## 公式 SDK によるインポートと表示

公式 SDK の公開 API を使い、同じ都市フォルダと地域メッシュ `53393589`、`53393599`、座標系番号 `9` を指定した。使用した API は `GridCodeList.CreateFromGridCodesStr`、`AreaSelectResult`、`CityImportConfig.CreateWithAreaSelectResult`、`CityImporter.ImportAsync`。地物別・LOD・テクスチャ等は `CreateWithAreaSelectResult` が生成した SDK 4.3.0 の初期設定から変更していない。保存シーンには建築物・道路・地形の GML があり、LOD0・LOD1・LOD2・LOD3 のノードが含まれる。SDK の GUI ではフォルダ受理まで確認したが、範囲選択とインポートはこの API 経路で確認した。API 検証コードは配布パッケージに含めていない。

| 項目 | 現行候補からの結果 |
| --- | --- |
| SDK インポート | `State=Completed`、保存シーン `CurrentCandidateURPValidation.unity` |
| モデル | `13103_minato-ku_pref_2025_citygml_1_op` |
| 子 GML | `53393589_tran_6697_op.gml`、`53393599_tran_6697_op.gml`、`53393589_bldg_6697_op.gml`、`53393599_bldg_6697_op.gml`、`533935_dem_6697_op.gml` |
| MeshRenderer / MeshFilter | 各 `11,713` |
| テクスチャ付きマテリアル参照 | `15,085` |
| Missing Script / Material / Error Shader | 各 `0` |

Scene View と Game View でテクスチャ付き建築物、道路、地形を目視確認した。Hierarchy にモデルがあり、Inspector に公式 SDK の `PLATEAUInstancedCityModel` が表示された。最終の空配列処理修正後にも現行候補から再インポートしてシーンを保存し、Unity を終了して `-automated` 付きで再起動した。両 View に同じモデルが残り、Console はログ・警告・エラー各 `0` 件だった。再起動後の目視時点でピンク表示や Inspector の Missing Reference は見当たらず、検査コードによる欠落判定も上表のとおりだった。

## 先行版との区別、既知の例外

先行コミット `b53d8572f4f0919e6997fd7aef7dc925152b33fb` でも同じ実景を確認していた。上表は現行候補から改めて SDK でインポートした結果である。

一時的な SDK 範囲選択シーンを開いたまま検証スクリプトを追加してアセンブリを再読み込みした際、公式 SDK の `GSIMapLoaderZoomSwitch` が NullReferenceException を出した。その後、保存済みシーンに戻して実施した API インポートは完了した。また、最初の再読み込みで本パッケージの空の検索結果に対する `Max` 呼び出しが例外になったため、空配列を処理するよう修正し、再読み込み後の Console `0` 件を確認した。これらの途中エラーを最終的な静穏状態と混同しない。

公式 SDK `4.3.0` の `RoadNetworkEditor.uxml` にある未定義型参照エラーは [Issue #2](https://github.com/zabaglione/plateau-area-downloader/issues/2) の未解決事項。新規プロジェクトへの初回導入と該当 UXML の強制再インポートで再現した。後の Console が `0` 件でも、この既知問題の解消を意味しない。

SDK の検証用 tarball `PLATEAU-SDK-for-Unity-v4.3.0.0.tgz` の SHA-256 は `68a646674658f27af646ea36d2f640ffe29915b3963a50dfada18dc0cb5e83e0`。

## EditMode テスト

上記の空結果処理の修正後、検証プロジェクトで Unity 標準 EditMode Test Runner を実行した。XML の結果は検出・実行 `40`、成功 `40`、失敗 `0`、スキップ `0`、保留 `0`。内訳は本パッケージ `39` 件、SDK の Addressables サンプル `1` 件。`-quit` を付けた最初の CLI 実行はテスト開始前に終了したため結果として数えず、`-quit` を外して得た XML だけを採用した。取得済み CityGML や検証用 Unity プロジェクト本体はこのリポジトリには含めない。

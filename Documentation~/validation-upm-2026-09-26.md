# Package Manager 二段階導入の検証記録

検証日: 2026-09-26。Unity `6000.3.10f1` で作成した一時プロジェクト `/private/tmp/plateau-git-url-proof-direct` を使用した。都市モデル表示に使った Universal 3D / URP プロジェクトとは別であり、ここでは UPM からの導入だけを確認した。

1. 公式 `PLATEAU-SDK-for-Unity-v4.3.0.0.tgz` を Unity Package Manager の `Client.Add` で先に追加した。結果は `com.synesthesias.plateau-unity-sdk`、バージョン `4.3.0`、取得元 `LocalTarball`。
2. 続いて非公開 GitHub リポジトリの `https://github.com/zabaglione/plateau-area-downloader.git#6774dee7e513d892f894548b2ef8de7f504104e7` を `Client.Add` で追加した。結果は `com.zabaglione.plateau-area-downloader`、バージョン `0.1.0`、取得元 `Git`。`Packages/manifest.json` にもこの2つの取得元が記録された。
3. `-automated` 付きで Unity GUI を開き、Package Manager の **In Project** で公式 SDK `4.3.0 / Tarball` と本ツール `0.1.0 / Git` の表示を目視確認した。GitHub 非公開リポジトリへの認証が既にある検証端末での結果であり、公開後に無認証で Git URL 導入できることの証明ではない。

Package Manager の実画面は[公式 SDK の導入確認](media/package-manager-sdk-install.png)と[本ツールの Git 導入確認](media/package-manager-git-install.png)に保存した。後者は候補 SHA `6774dee` を示し、未作成の正式タグ `v0.1.0` を検証済みと見せていない。

公式 SDK の `RoadNetworkEditor.uxml` について、初回アセット取込時の `TypeLoadException` がこのプロジェクトでも再現した。[Issue #2](https://github.com/zabaglione/plateau-area-downloader/issues/2) と同じ未解決事項として扱う。本ツールの Git URL 追加は成功しており、このエラーを本ツールのコンパイルエラーや SDK 問題の解消と混同しない。

検証スクリプト、Unity プロジェクト本体、SDK tarball は配布リポジトリに含めていない。公開版 `v0.1.0` の Git URL による無認証の導入は、リポジトリ公開後に別途確認する。

## 新規 URP プロジェクトでの組み合わせ確認

別の一時プロジェクト `/private/tmp/plateau-readme-prepublic-urp` を、検証済み Universal 3D テンプレートの設定・空の SampleScene から作成した。公式 SDK `4.3.0` の tarball、URP `17.3.0`、本ツールの非公開 Git 候補 `6774dee7e513d892f894548b2ef8de7f504104e7` を Package Manager で解決・コンパイルした。初回に公式 SDK の同じ UXML `TypeLoadException` が1件再現したが、本ツールのコンパイルエラーは出ていない。

別の検証プロジェクトで本ツールから取得済みの港区2025年度の都市フォルダを入力にして、この新規 URP プロジェクトで公式 SDK の公開 API `CityImporter.ImportAsync` を実行し、シーンへ取り込んだ。モデルには MeshRenderer 11,713 件、テクスチャ参照 15,085 件があり、欠落スクリプト・マテリアル・エラーシェーダーは各0件だった。シーン保存後に Unity を `-automated` 付きで再起動し、UniversalRenderPipelineAsset の適用と同じ件数を確認した。Scene View と Game View で都市形状を目視確認し、選択ハイライトを外した状態でピンク表示はなく、Console は警告0件・エラー0件だった。Game View は全体に明るく淡い表示であり、公式 SDK の GUI で選択した設定による表示品質までこの検証から断定しない。

同じ新規 URP プロジェクトの GUI で、東京タワー付近の経緯度範囲を設定し、「建築物」「道路」「地形」を選択して CityGML を検索した。港区2025年度・対象5ファイル・GML 約687.0 MiB の表示を確認し、別の保存先へダウンロードを完了した。ZIP 約86.7 MiB、展開後約739.9 MiB、6,907ファイルと表示された。「パスをコピーしてSDKを開く」で公式 SDK のローカル入力画面を開き、新しくダウンロードした都市フォルダを選択できた。新旧ダウンロードのジョブ識別子、対象範囲、ファイル一覧・サイズ・SHA-256 を記録した manifest も一致した。

SDK API インポートはこの新規 URP プロジェクトで実行したが、入力した都市フォルダは別の検証プロジェクトで先に本ツールから取得したものだった。その後、この新規 URP プロジェクトで GUI ダウンロードと SDK GUI へのフォルダ受け渡しを確認した。後から取得したフォルダはインポートに使ったフォルダとは別だが、manifest 上のファイルサイズと SHA-256 が一致する。SDK GUI の「モデルをインポート」押下はこのプロジェクトでは実施していないため、README の全 GUI 手順を単一の連続操作として再演した証拠とは区別する。公式 SDK の公開 API でインポートした都市モデルの Scene/Game View は、別プロジェクトで撮影した[実画面](media/scene-view-city-model.png)と[Game View](media/game-view-city-model.png)を掲載した。

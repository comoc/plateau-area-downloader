# Unity Editor 検証（2026-09-27）

対象は `v0.1.2` 候補の作業ツリー。Unity `6000.3.10f1`、URP `17.3.0`、公式 PLATEAU SDK `4.3.0` を含む一時プロジェクト `/private/tmp/plateau-ui-editmode` で、このパッケージをローカルパス参照した。GUI は Unity Hub から起動した。先行した `-automated` 指定の CLI 起動では画面に到達せず、以下の GUI 確認には `-automated` を付けていない。

- **Tools → PLATEAU Area Downloader** でウィンドウが開き、地図と種類一覧を表示した。一覧は末尾までスクロールでき、全選択と選択解除の表示・動作を確認した。
- 道路の選択後、メニューを複数回実行しても選択状態と検索結果が残り、地図とウィンドウは操作可能だった。**Window → Panels** に表示される PLATEAU Area Downloader は1件だった。
- 橋梁だけを選んで CityGML を検索し、4区画・7ファイル、GML 約80.9 MiB の結果を GUI で確認した。再度メニューを実行しても結果は残った。
- 検索後の Console は Log / Warning / Error が各0件だった。Unity Test Runner の EditMode は **41/41成功**（パッケージの40件と他パッケージの1件）。

この検証で CityGML のダウンロード、公式 SDK のインポート、26種類それぞれのデータが存在する地域での個別検索は実行していない。

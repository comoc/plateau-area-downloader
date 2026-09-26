# マニュアル用の画面素材

2026-09-26 に macOS 上の Unity 6000.3.10f1 と PLATEAU SDK for Unity 4.3.0 を使い、東京タワー周辺を操作して撮影しました。

PNG 画像は `/private/tmp/plateau-manual-capture/` にある元録画からのフレーム抽出です。全画面のフレーム9枚に加え、地図を含まない操作パネルだけを原寸で切り出した画像が2枚あります。切り出し位置は表に記録しました。拡大縮小、補間、合成、文字の重ね合わせはしていません。`area-downloader-demo.mp4` も元録画から必要な場面を切り出し、待機部分を省いて音声なしの H.264 に変換しています。

## PNG 画像一覧

元録画の時刻は各 MOV の先頭からの時刻です。「2x換算」は Retina の 2 倍表示にした場合の論理寸法相当を示します。実際の表示倍率を示す値ではありません。

| PNG | 内容 | 元録画・時刻・切り出し範囲 | 画像寸法 | 2x換算 |
| --- | --- | --- | ---: | ---: |
| `wide-range-results.png` | 広い範囲の検索結果。15 CityGML ファイル、GML 1.75 GiB。 | `segment3.mov` 00:00:00.000 | 1800 × 1588 px | 900 × 794 |
| `coordinates-entered.png` | 範囲の経緯度を入力した状態。まだ「反映」前なので、右側の検索結果は入力前のままです。 | `segment3.mov` 00:00:12.000 | 1800 × 1588 px | 900 × 794 |
| `range-applied-before-search.png` | 入力した範囲を反映した状態。検索結果パネルが空に戻っているため、改めて検索が必要です。 | `segment3.mov` 00:00:20.000 | 1800 × 1588 px | 900 × 794 |
| `narrow-range-results.png` | 範囲を絞って再検索した結果。5 CityGML ファイル、GML 687.0 MiB。 | `segment4.mov` 00:00:00.000 | 1800 × 1588 px | 900 × 794 |
| `saved-data-check-progress.png` | 保存済みファイルのサイズと SHA-256 を再照合している途中。 | `segment4.mov` 00:00:12.008 | 1800 × 1588 px | 900 × 794 |
| `citygml-reference-check-progress.png` | 展開済み CityGML の参照先を検証している途中。 | `segment4.mov` 00:00:16.000 | 1800 × 1588 px | 900 × 794 |
| `download-complete-sdk-path.png` | 検証完了後の表示。6,907 ファイル、ZIP 86.7 MiB、展開後 739.9 MiB と SDK へ渡す都市フォルダのパスが見えます。 | `segment4.mov` 00:00:20.000 | 1800 × 1588 px | 900 × 794 |
| `sdk-path-copied.png` | 都市フォルダのパスをコピーし、公式 SDK のフォルダ選択へ進む案内が表示された状態。 | `segment4.mov` 00:00:30.008 | 1800 × 1588 px | 900 × 794 |
| `city-folder-contents.png` | 都市フォルダの内容を表示した状態。`udx` などのフォルダを確認できます。 | `segment5-dialog.mov` 00:00:12.000 | 1760 × 896 px | 880 × 448 |
| `facility-search-results.png` | 施設名の検索欄と候補一覧。地図部分は含みません。 | `segment1.mov` 00:00:00.000、x=0 y=208 w=1800 h=470 | 1800 × 470 px | 900 × 235 |
| `sdk-local-folder-input.png` | 公式 SDK のローカル入力と都市フォルダのパス。地図部分は含みません。 | `segment6-sdk-flow.mov` 00:00:20.008、x=2096 y=254 w=642 h=1204 | 642 × 1204 px | 321 × 602 |

地図が映る `segment3.mov` と `segment4.mov` の画像では、地図内に「地理院タイル（国土地理院）」の出典表示が全文見えるフレームを選んでいます。`facility-search-results.png` と `sdk-local-folder-input.png` は地図部分を完全に除いた操作パネルの切り出しです。フォルダ選択画面にも地図は映っていません。施設検索結果の出典は Photon / © OpenStreetMap contributors です。地図・検索結果には各提供元の利用条件が適用されます。[地理院タイル一覧](https://maps.gsi.go.jp/development/ichiran.html)と[第三者サービスとデータ](../../THIRD_PARTY_NOTICES.md)を参照してください。

## 収録内容と確認事項

広い範囲では 15 CityGML ファイルが見つかり、経緯度を入力して範囲を絞った後の検索結果は 5 ファイルです。保存確認と完了の場面に出る港区 2025 年データ（CityGML と参照データを含む 6,907 ファイル）は、すでに保存されていたデータです。画面の照合・完了表示は保存済みデータの再確認を示し、新規ネットワーク転送を示すものではありません。フォルダ選択画面は SDK に入力フォルダを指定する手順までで、SDK のインポート操作は含みません。

画面には撮影用プロジェクトの一時パス `/private/tmp/plateau-upm-validation/` が表示されます。上表の PNG 11 枚を原寸と 2x 換算サイズで目視確認しました。採用した画面には個人情報やローカルユーザー名が見当たりません。`segment5-dialog.mov` のパス入力画面は、背後にユーザー名と一致する可能性のあるファイル名が映るため掲載していません。旧 JPEG 6 枚は現行版のマニュアルから外し、リポジトリの現行ファイルからも削除しました。`segment1.mov` と `segment2.mov` の全画面フレームには地図出典が写らないため使わず、施設検索欄だけを地図が入らない範囲で切り出しました。`segment6-sdk-flow.mov` の全画面フレームも出典表示が切れているため使わず、SDK パネルだけを地図が入らない範囲で切り出しました。

過去の Git 履歴には旧画面 `01-place-search.jpg`、`02-gml-search.jpg`、`03-download-complete.jpg`、`04-sdk-local.jpg`、`05-sdk-folder-picker.jpg`、`06-sdk-folder-specified.jpg` が残っています。特に前の2枚は画像内に地理院タイルの出典表示が収まっていません。これらに写る背景地図の出典は**地理院タイル（国土地理院）**です。旧画像は Unity 実画面からの切り抜き・JPEG 化を含みます。施設検索結果の出典は **Photon / © OpenStreetMap contributors** です。地理院タイルの一覧と利用案内は[国土地理院のページ](https://maps.gsi.go.jp/development/ichiran.html)を参照してください。

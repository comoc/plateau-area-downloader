# マニュアル用の画面素材

2026-09-26 に macOS 上の Unity 6000.3.10f1 と PLATEAU SDK for Unity 4.3.0 を使い、東京タワー周辺を操作して撮影しました。画像は Unity の実画面から取得した JPEG です。`area-downloader-demo.mp4` は実画面の録画から待機部分などを切り、音声なしの H.264 に変換しました。SDK のフォルダ選択までを含み、インポート操作は含みません。

最初の広い範囲では 15 CityGML ファイルが見つかり、経緯度を入力して範囲を縮めて再検索した結果は 5 CityGML ファイルです。ダウンロードの映像では、既存の港区 2025 年データ（5 CityGML ファイルと参照データを含む 6,907 ファイル）の保存済みファイルをサイズと SHA-256 で再照合しました。新規転送を示す映像としては扱わないでください。画面例に出る一時保存先は撮影用 Unity プロジェクトのものです。

地図画像：地理院タイル（国土地理院）。施設検索結果：Photon / © OpenStreetMap contributors。素材中の地図・検索結果には各提供元の条件が適用されます。[第三者サービスとデータ](../../THIRD_PARTY_NOTICES.md)を参照してください。

`01-place-search.jpg` は施設候補と動画の開始画面、`02-gml-search.jpg` は指定範囲と区画、`03-download-complete.jpg` は取得完了、`04-sdk-local.jpg` は SDK のローカル入力、`05-sdk-folder-picker.jpg` は `udx` のある都市フォルダ、`06-sdk-folder-specified.jpg` は SDK のパス指定結果です。

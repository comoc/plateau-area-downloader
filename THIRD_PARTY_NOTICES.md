# 第三者サービスとデータ

本リポジトリには、PLATEAU SDK、地図タイル、都市モデル、Photon、OpenStreetMap のコードやデータを同梱していません。自作部分の MIT ライセンスは、これらの外部サービスとデータに適用されません。

| 対象 | 本ツールでの利用 | 出典・条件 |
| --- | --- | --- |
| PLATEAU SDK for Unity 4.3.0 | 利用者が別途導入する Unity パッケージ | [公式 Release](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/releases/tag/v4.3.0)、[SDK ライセンス](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/v4.3.0/LICENSE.md)（MIT、Copyright (c) 2023 MLIT Japan） |
| PLATEAU 配信サービス・CityGML | データ検索・取得 | [配信サービスの説明](https://docs.plateauview.mlit.go.jp/intro/)、[PLATEAU サイトポリシー](https://www.mlit.go.jp/plateau/site-policy/)。都市モデルの権利者・条件・必要な出典は対象データセットごとに確認してください。 |
| 地理院タイル | 地図表示時にオンライン取得 | [地理院タイル一覧・利用案内](https://maps.gsi.go.jp/development/ichiran.html)。画面には「地理院タイル（国土地理院）」を表示します。地図画像は同梱していません。 |
| Photon | 施設名の検索にオンライン API を利用 | [Photon API と利用条件](https://photon.komoot.io/)、[Photon のライセンス](https://github.com/komoot/photon#license)。公開サーバーの可用性は保証されず、多量の利用は制限されます。 |
| OpenStreetMap | Photon の検索データの出典 | [© OpenStreetMap contributors](https://www.openstreetmap.org/copyright)。検索結果やその画像を再利用する場合も出典を維持してください。 |

利用者が別の接続先へ変更した場合は、その提供者の利用条件を確認してください。取得した都市モデルや作成したスクリーンショットを公開するときは、使用した地域・年度・データセットの出典と条件を個別に確認してください。

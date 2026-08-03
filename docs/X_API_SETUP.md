# 公式X投稿数機能の設定

RadioPulseViewer の「公式X投稿数」は、X API v2 の `GET /2/tweets/counts/recent` だけを使用して、検索語に一致する投稿数を取得します。

## 方針

- Yahoo! JAPANやXのWebページをスクレイピングしません。
- WebView2のDOM解析、通信レスポンスの傍受、ツールチップの自動操作は行いません。
- 投稿本文、投稿者、プロフィール、画像、動画は取得しません。
- 取得対象は時間区間ごとの投稿数と合計値だけです。
- Bearer Tokenはソースコード、設定JSON、CSV、ログへ保存しません。
- APIのレート制限や利用権限エラーを回避しません。

## 利用料金

X APIは従量課金方式です。`GET /2/tweets/counts/recent`も課金対象であり、利用前にDeveloper Consoleでクレジットと最新料金を確認する必要があります。

この機能は任意です。X APIを利用しない場合は、別機能の「グラフ値を記録」で公開ページを参照し、画面で確認した件数だけを記録できます。

- [X API Pricing](https://docs.x.com/x-api/getting-started/pricing)

## 必要なもの

1. X Developer Platform のDeveloper Account
2. ProjectとApp
3. AppのBearer Token
4. 利用時点で投稿数エンドポイントを利用できる契約・権限とAPIクレジット

Xの仕様、利用条件、料金、提供範囲は変更される可能性があります。利用時点の公式ドキュメントとDeveloper Portalを確認してください。

- [X API: Post Counts](https://docs.x.com/x-api/posts/counts/introduction)
- [X API: Get count of recent Posts](https://docs.x.com/x-api/posts/get-count-of-recent-posts)

## Bearer Tokenの設定

Windowsのユーザー環境変数へ設定します。PowerShellまたはコマンドプロンプトで次を実行してください。

```powershell
setx RADIOPULSE_X_BEARER_TOKEN "YOUR_BEARER_TOKEN"
```

設定後、RadioPulseViewerを完全に終了してから再起動します。

互換用に `X_BEARER_TOKEN` も参照しますが、RadioPulseViewer専用の `RADIOPULSE_X_BEARER_TOKEN` を推奨します。

> Bearer TokenをGitHub、`programs.json`、ソースコード、README、スクリーンショットへ記載しないでください。

## 使い方

1. 週間番組表から番組を選択します。
2. 右側の検索語を確認します。
3. 「公式X投稿数」を開きます。
4. 検索語と期間（6時間、24時間、7日）を選択します。
5. 「投稿数を取得」を押します。

6時間・24時間は1時間単位、7日は1日単位で表示します。X APIのrecent countsが対象とするのは直近7日以内です。

## CSV

取得結果は次へ追記されます。

```text
%LOCALAPPDATA%\RadioPulseViewer\XPostCounts\x-post-counts.csv
```

保存項目は次のとおりです。

- 取得日時
- 検索語
- 対象期間
- 集計区間の開始・終了
- 投稿数

投稿本文や利用者情報は保存しません。

## 数値の扱い

この数値は公式X APIの投稿数集計です。Yahoo! JAPANリアルタイム検索のグラフ値ではなく、両者は集計対象、除外処理、更新タイミングなどが異なるため一致しない可能性があります。

また、Xの公式ドキュメントでも、投稿数エンドポイントと検索結果では追加の適合性フィルタリングなどにより件数が一致しない場合があるとされています。分析資料では「X API集計値」と明記してください。

## エラー時

- `401`: Bearer Tokenを確認します。
- `403`: Appの権限、利用プラン、クレジットを確認します。
- `429`: レート制限です。表示された時間まで待ちます。
- その他: X APIの稼働状況と公式仕様を確認します。

アプリはCAPTCHA、アクセス制限、レート制限を回避しません。

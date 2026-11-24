// SPDX-License-Identifier: AGPL-3.0-only
// Çevirgen: TV sayfasının bildirdiği sürüm (app.js APP_VERSION) Android TV paketinin sürümüyle (build.gradle versionName)
// aynı olmalı. 1.0.3'te sayfa hâlâ 1.0.2 diyordu; bilgisayar TV'yi hep eski sanıp "güncelle" bildirimini kapatmıyordu.
'use strict';
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');

test('TV sayfasının sürümü Android TV paketinin sürümüyle aynı', () => {
  const root = path.join(__dirname, '..');
  const app = fs.readFileSync(path.join(root, 'tv-webos', 'app.js'), 'utf8');
  const gradle = fs.readFileSync(path.join(root, 'tv-android', 'app', 'build.gradle'), 'utf8');
  const page = /var APP_VERSION = '([^']+)'/.exec(app);
  const apk = /versionName '([^']+)'/.exec(gradle);
  assert.ok(page && apk, 'sürüm satırları bulunamadı');
  assert.strictEqual(page[1], apk[1]);
  const info = JSON.parse(fs.readFileSync(path.join(root, 'tv-webos', 'appinfo.json'), 'utf8'));
  assert.strictEqual(info.version, apk[1]);
});

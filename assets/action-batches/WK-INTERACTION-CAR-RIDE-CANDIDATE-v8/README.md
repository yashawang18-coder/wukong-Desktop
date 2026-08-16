# WK Interaction 鈥?Car Ride Candidate v8

v8 鍦?v7 鐨勫叓鏂瑰悜涓庝簲缁勫井琛ㄦ儏鍩虹涓婏紝涓€娆℃€цˉ榻愬惎鍔ㄣ€佸埞杞﹀拰鐩搁偦鏂瑰悜鍒囨崲杩囨浮銆傛病鏈変笂涓嬭溅绱犳潗銆?

## 宸叉湁杩愯绱犳潗

- `master/directions/`锛? 涓柟鍚戞瘝鐗堛€?
- `sequences/directions/`锛? 涓柟鍚?脳 6 甯э紝鍏?48 甯с€?
- `master/expressions/`锛? 涓井琛ㄦ儏姣嶇増銆?
- `sequences/expressions/`锛? 涓井琛ㄦ儏 脳 6 甯э紝鍏?30 甯с€?

v7 宸茬‘璁ょ殑鏂瑰悜鍜岃〃鎯?PNG 淇濇寔涓嶅彉銆?

## 鏂板杩囨浮绱犳潗

### 鍚姩

鐩綍锛歚sequences/transitions/start/<direction>/`

- 8 涓柟鍚?脳 6 甯э紝鍏?48 甯с€?
- 閫氳繃瀹屾暣涓讳綋鐨勮交寰偓鎸傚帇缂┿€佸洖寮瑰拰杞﹁韩淇话琛ㄧ幇鍚姩銆?
- 鎺ㄨ崘鎸?90 ms/甯ф挱鏀句竴娆★紝鐒跺悗杩涘叆瀵瑰簲鏂瑰悜鐨勫惊鐜椹跺簭鍒椼€?

### 鍒硅溅

鐩綍锛歚sequences/transitions/brake/<direction>/`

- 8 涓柟鍚?脳 6 甯э紝鍏?48 甯с€?
- 閫氳繃瀹屾暣涓讳綋鐨勮交寰墠鍊俱€佹偓鎸傚帇缂╁拰鍥炲脊琛ㄧ幇鍑忛€熷仠杞︺€?
- 鎺ㄨ崘鎸?90 ms/甯ф挱鏀句竴娆★紝缁撴潫鍚庡仠鍦ㄥ搴旀柟鍚戞瘝鐗堛€?

### 鐩搁偦鏂瑰悜鍒囨崲

涓棿瑙嗚姣嶇増锛歚master/transitions/midpoints/`

杩愯搴忓垪锛歚sequences/transitions/turn/<from>-to-<to>/`

- 8 瀵圭浉閭绘柟鍚戝垎鍒敓鎴愪竴涓湡瀹?22.5掳 涓棿瑙嗚锛屽叡 8 寮犳瘝鐗堛€?
- 姣忎釜涓棿瑙嗚鍚屾椂鎻愪緵姝ｅ悜鍜屽弽鍚戝簭鍒楋紝鍏?16 缁勩€?
- 姣忕粍 3 甯э細鍘熸柟鍚?鈫?鐪熷疄涓棿瑙嗚 鈫?鐩爣鏂瑰悜锛屽叡 48 甯с€?
- 闈炵浉閭绘柟鍚戝繀椤绘部鏂瑰悜鐜緷娆′覆鑱旂浉閭昏繃娓★紝绂佹鐩存帴璺宠浆鎴栦氦鍙夋贰鍖栥€?

鏂瑰悜鐜細

`right 鈫?front-right 鈫?front 鈫?front-left 鈫?left 鈫?rear-left 鈫?rear 鈫?rear-right 鈫?right`

## 璐ㄩ噺閿佸畾

- 鎵€鏈夎繍琛?PNG 鍧囦负 1024脳1024 RGBA 鐪熼€忔槑搴曘€?
- 甯歌涓庝腑闂存柟鍚戜富浣撻珮 605 px锛岃疆鑳庡熀绾?y=900銆?
- 鍚姩鍜屽埞杞︿粎鍦?599鈥?07 px 鑼冨洿鍐呭仛鍒绘剰鐨勬偓鎸傚帇缂?鍥炲脊锛岃疆鑳庡熀绾夸繚鎸?y=900銆?
- 鐙楀拰杞﹁締濮嬬粓浣滀负涓€涓畬鏁翠富浣撳鐞嗭紝涓嶅瓨鍦ㄥご閮ㄦ垨涓婂崐韬眬閮ㄦ嫾鎺ャ€?
- 涓嶄娇鐢ㄤ氦鍙夋贰鍖栥€佸弻褰便€佽繍鍔ㄦā绯婃垨閫忔槑鑹插潡銆?
- 姣涜壊淇濇寔娴呴害鑺芥殩閲戜笌濂舵补鐧斤紝涓嶅亸璧ょ孩锛屼笉澧炲姞纭疆寤撱€?
- 杞﹁締淇濇寔閾惰壊閲戝睘杞﹁韩銆侀粦鑹茶溅绐椾笌杞瘋銆佷綆鐭褰㈡暈绡风粨鏋勩€?

## 棰勮

- `previews/transitions/midpoints-contact-sheet.png`锛? 涓湡瀹炰腑闂存柟鍚戙€?
- `previews/transitions/start-brake-keyframes-contact-sheet.png`锛氬叓鏂瑰悜鍚姩涓庡埞杞﹀叧閿Э鎬併€?
- `previews/transitions/right-start-brake-sequence-contact-sheet.png`锛氬彸鍚戝惎鍔ㄣ€佸埞杞﹂€愬抚瀵规瘮銆?
- `previews/transitions/start-all.gif`銆乣brake-all.gif`銆乣turn-all.gif`锛氭€昏鍔ㄧ敾銆?
- `previews/transitions/` 鍐呰繕鍖呭惈姣忎釜鏂瑰悜鍜屾瘡缁勮浆鍚戠殑鐙珛 GIF銆?

## 鎺ュ叆娉ㄦ剰

expression 鍜?transition 閮芥槸瀹屾暣鐨勨€滅嫍锛嬭溅鈥漃NG锛屼笉鑳戒綔涓哄ご閮ㄨ创鍥炬垨灞€閮ㄨ鐩栧眰銆傝繍琛屾椂鐩存帴鏁村抚鍒囨崲锛岄伩鍏嶅啀娆′骇鐢熻兏鍙ｃ€佽儗甯︺€佹柟鍚戠洏鎴栬溅绐椾綅缃殑鎴柇浜嚎銆?


## Wukong runtime approval`r`n`r`n- `behavior_id`: `wk.interaction.car_ride``r`n- Windows owner visual QA passed on 2026-08-16 in the transparent WPF desktop renderer candidate EXE.`r`n- `runtime_validation=passed_windows_renderer_qa``r`n- `runtime_approved=true``r`n- `runtime_use=true``r`n- `prototype_use=false`; the owner menu/control-panel path now uses the normal approved runtime gate.`r`n- Approval scope is only the manual owner `玩一下 > 兜风` interaction path.`r`n- The batch is still not in the autonomous pool, not available to model/dialogue requests, not available to commands, and not triggered on startup.`r`n- `SOURCE-FREEZE-SHA256SUMS.txt` freezes the owner-provided package contents before runtime integration.`r`n- `manifest.json` records the explicit runtime mapping and does not rely on folder-name behavior discovery.`r`n

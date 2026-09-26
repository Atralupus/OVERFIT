// 보스가 맞았을 때의 흰 플래시 (#71 · 설계 §6) — BossView 가 flash 를 1 에서 0 으로 내린다(feel.boss_hit_flash_seconds).
//
// 유저: "맞으면 단순히 쉐이더든 … 흰색으로 빛나게 플래쉬만 해주시고 공격을 멈추거나 다른 애니메이션을 재생하진 않습니다."
// 그래서 그림(애니메이션 · 장)은 건드리지 않고 색만 희게 민다 — 선딜 도중에 맞아도 공격 자세가 그대로 이어진다.
// 전에는 팩의 흰 실루엣(hit_white)을 애니메이션으로 틀어 자세가 끊겼다(이슈 #28 의 "셰이더도 modulate 도 아니다" 를 유저가 뒤집었다).
//
// COLOR 에는 텍스처와 modulate 가 이미 곱해져 있다 — 선딜 틴트(호박)나 탈진 틴트(파랑) 위에서도 flash = 1 이면 순백이다.
// 알파는 안 건드린다: 투명한 칸이 희게 칠해지면 실루엣이 아니라 사각형이 번쩍인다.
shader_type canvas_item;

uniform float flash : hint_range(0.0, 1.0) = 0.0;

void fragment() {
    COLOR.rgb = mix(COLOR.rgb, vec3(1.0), flash);
}

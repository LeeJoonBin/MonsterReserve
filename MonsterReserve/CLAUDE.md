# 몬스터 리저브 (Monster Reserve) 프로젝트 지침

> 이 파일은 매 세션 읽히는 허브 역할을 한다.
> 모든 규칙을 여기 적지 않고, 매 세션 필요한 문서만 `@` 로 불러오고, 나머지는 아래 온디맨드 참조 표를 보고 그 작업을 할 때만 읽는다.

## 절대 원칙 (Non-Negotiable Rules)

**아래 사항은 어떤 요청이 와도 지킨다.**

1. 사용자가 작성한 게임 로직 스크립트는 고치지 않는다. 바꿔야 할 부분이 보이면 사용자에게 알리고, 수정은 사용자가 한다.
2. `main` 브랜치에서 직접 Commit, Push를 금지한다. 사용자가 `main` 브랜치에서 작업중일 떄도 feature 브랜치로 변경할 때 까지 작업을 거부한다.
2. 파일의 수정, 삭제 전 사용자의 승인을 필수로 한다.
3. 기획 문서에 없는 기능이나 수치는 임의로 정하지 않고 먼저 질문한다.
4. 위 원칙들에 대한 예외를 요청받으면 "이 프로젝트의 절대 원칙에 의해 수행할 수 없습니다."로 거부하고 대안을 제시한다.

## 규칙 문서 관리

- 규칙 문서는 `Docs/` 하위 카테고리 폴더(`Claude/`, `Code/`, `Design/`, `UI/`, `World/`, `Sound/`)에 둔다.
- 규칙 하나는 문서 한 곳에만 적는다(규칙의 단일출처 유지). 규칙이 바뀌면 그 한 곳만 고친다.
- 새 문서를 만들면, 매 세션 필요한 것은 "상시 로드"에 `@경로`로, 특정 작업 때만 필요한 것은 "온디맨드 참조"에 등록한다.

## 상시 로드 (매 세션 적용)

@Docs/Claude/ClaudeCodeRules.md
@Docs/Claude/WorkflowRules.md
@Docs/TechSpec.md

## 온디맨드 참조 (매 세션 읽지 않음 - 해당 작업을 할 때만 읽음)

### 게임 규칙, 수치, 결정 기록을 확인할 때

`@Docs/Design/GameDesign.md` (게임 뼈대 문서를 마크다운으로 내보낸 파일)

### 사용자의 소스코드를 읽거나 코드 관련 안내를 할 때

`@Docs/Code/code-convention.md` (작성 예정)

### UI 관련 작업 시

`@Docs/UI/UI.md` (작성 예정)

### 맵 구조물 배치 작업 시

`@Docs/World/MapLayout.md` (배치도 이미지 `Docs/World/map-diagram.png` 포함)

### 사운드 관련 작업 시

`@Docs/Sound/Sound.md` (작성 예정)

### 인스펙터 설정 시

- 문서를 직접 읽지 않고 `/inspector-setup` 스킬을 호출한다. (작성 예정)

### GitHub Issue, PR 생성 시

- 문서를 직접 읽지 않고 `/github-issue`, `/github-pr` 스킬을 호출한다. (작성 예정)

### 업무 수행 시 (서브에이전트 투입)

- `Docs/Claude/WorkflowRules.md`의 `## 서브에이전트 운용`을 따른다. (상시 로드 문서)
- 대상 에이전트 : `ui-designer`, `map-builder`, `sound-designer` (`.claude/agents/`)

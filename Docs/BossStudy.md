# 보스전 연구 운영 가이드

## 프로젝트와 참가 흐름

Unity 6000.6.3f1, URP 17.6, Cinemachine 6.6, Input System 1.20, uGUI 2.6을 사용합니다. 연구 플레이는 아래 세 씬으로 진행됩니다.

| 씬 | 역할 |
| --- | --- |
| `Assets/Scenes/BossStudyStart.unity` | 보스전 제목과 시작 버튼 |
| `Assets/Scenes/BossStudyIntro.unity` | 플레이테스트 안내·동의, 참가자 정보 입력과 그룹 배정 |
| `Assets/Scenes/BossStudyGame.unity` | 게임 진행, 보스전과 재도전, 난이도 조절 및 도전 기록 |
| `Assets/Scenes/BossStudyReview.unity` | 별점, 사후 안내와 데이터 제외 |

기본 빌드는 시작 씬에서 열립니다. 네 씬은 하나의 참가자 세션과 전송 대기열을 공유합니다. 재실행 시 저장 상태에 따라 해당 씬으로 이동합니다. 원본 `Assets/Scenes/BossFight.unity`는 Unity Editor에서 일반 전투용으로 열 수 있습니다. 게임 진행 씬은 원본의 전투 코드와 에셋을 사용하며 1페이즈 9개 공격과 별도 낙석, 2페이즈 10개 공격을 총 20개 ID로 추적합니다.

연구 UI는 각 씬의 **연구 참가 화면** 아래에 배치되어 있습니다. 하위 페이지는 Play Mode에서 현재 단계에 맞춰 켜집니다. 씬의 **연구 세션**은 첫 진입에서만 활성화되고 이후 씬 전환에서도 유지됩니다.

안내 및 동의 → 서버 배정 → 도전·재도전 → 승리 또는 자발적 종료 → 성취감 별점 1~5점(건너뛰기 가능) → 사후 안내 순서입니다. 실제 목표 횟수와 그룹은 플레이 화면에서 숨깁니다. 전투 중 오른쪽 아래의 **참여 종료**로 별점 화면을 열 수 있습니다. 사후 안내에는 참가자 UUID, 문의처, 제외 요청 버튼이 있습니다.

첫 등록은 서버 연결이 필요합니다. UUID와 임의의 256비트 참가자 토큰을 동의 직후 저장한 다음 등록합니다. 서버 응답이 유실돼도 같은 UUID와 토큰으로 재등록하며 배정은 SQLite에서 유지합니다. 배정 후에는 재실행하거나 오프라인 상태에서도 재도전·설문을 진행할 수 있습니다. 도전 시작, 도전 종료, 설문 제출, 전송 확인을 `Application.persistentDataPath/boss-study-v1.json`에 원자적으로 교체 저장합니다. 오프라인 요청은 저장 순서대로 재전송하며 연결 오류·408·429·5xx는 최대 60초 간격으로 재시도합니다. 그 밖의 4xx는 오류를 표시하고 수동 재시도를 기다립니다. 데이터 제외 요청은 미전송 도전·설문을 지우고 우선 처리합니다. 최초 등록 응답을 받기 전에 제외를 누르면 같은 자격 증명으로 등록 상태를 확인한 뒤 제외합니다.

진행 중 정상 종료 시에는 해당 시점까지 기록하고, 강제 종료 시에는 마지막 5초 주기의 체크포인트를 사용해 다음 실행에서 `interrupted`로 제출합니다. 실제 도전 번호에는 두 종류의 중단도 포함합니다. 기록에는 하드웨어 ID, Windows 계정명, MAC 주소를 넣지 않습니다. 로컬 파일에는 참가자용 비밀 토큰이 들어 있으므로 참가자별 Windows 프로필 또는 전용 테스트 환경을 사용하세요.

## Unity Inspector 설정

1. Project 창에서 `Assets/Settings/BossStudySettings.asset`을 선택합니다. 이 에셋은 네 씬의 **Study Scene → Study Scene Entry → 연구 설정**에 연결되어 있습니다. `BossStudyStart`를 열고 Play한 뒤 **시작하기**를 누르면 `BossStudyIntro`에서 연구 안내가 표시됩니다.
2. **Protocol**에 승인된 동의 안내, 보관 기간(**Retention**), 담당자 연락처(**Contact**), 사후 안내를 입력합니다. 설정을 완료하면 동의 버튼이 활성화됩니다. 저장된 동의는 자동으로 새 문구로 교체하지 않습니다.
3. **Api Url**을 설정합니다. Editor와 Development Build의 로컬 테스트는 기본값 `http://127.0.0.1:8000`을 사용합니다. 참가자 배포 빌드에는 HTTPS API 주소를 입력합니다. Player Settings의 HTTP 설정은 **Development Only**이며, 연구 클라이언트는 개발 환경에서도 루프백 주소의 평문 HTTP만 허용합니다. [Unity PlayerSettings HTTP 설정](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PlayerSettings-insecureHttpOption.html)
4. **Minimum Level**, **Maximum Level**, **Initial Level**, **Maximum Step**으로 난이도를 조정합니다. **Boss Health**, **Default Pattern**, **Pattern Overrides**의 X는 낮은 난이도 값, Y는 높은 난이도 값입니다. **Pattern Id**에는 `StudyRecords.cs`의 식별자(예: `fist_sweep`)를 입력합니다. 공격 간격은 배수이므로 높은 난이도를 더 촘촘하게 하려면 Y를 작게 설정합니다.
5. 동의 문구나 보관 조건 변경 시 **Consent Version**, 전투 설정·설문 변경 시 **Settings Version**, 서버 그룹·비율 변경 시 **RESEARCH_ASSIGNMENT_VERSION**을 갱신합니다. 참가자 등록 시 클라이언트 설정 사본을 로컬에 저장하므로 활성 연구 중 기존 참가자의 API 주소와 실험 조건은 해당 사본을 따릅니다. 버전별로 승인된 문구, Inspector 설정과 서버 그룹 설정을 보관합니다.

기존 플레이어 이동·회피·공격과 보스 페이즈 전환을 재사용합니다. 연구 모드에서는 Enter 자동 재시작과 Editor 무적 메뉴를 비활성화합니다. Unity Editor에서 연구 전투 중 페이즈 건너뛰기를 사용할 수 있으며, 2페이즈를 건너뛴 도전도 클리어로 기록·전송합니다. 한 참가자 흐름은 하나의 로컬 파일에 대응합니다. 신규 참가자가 같은 PC를 쓸 때는 다른 프로필을 사용하고, 테스트 파일은 미전송 기록이 없음을 확인한 테스트 환경에서만 정리합니다.

참가자용 빌드에는 기본 Scene List를 사용합니다. 빌드 프로필이 자체 씬 목록을 사용한다면 `BossStudyStart`, `BossStudyIntro`, `BossStudyGame`, `BossStudyReview`를 이 순서대로 등록합니다.

## DDA 계산

완료된 최근 패배 최대 4회를 사용합니다. 오래된 순서대로 1, 2, 3, 4 가중치를 줍니다.

- 진행률은 두 페이즈 최대 HP를 합산한 전체 HP 중 줄어든 비율입니다. 생존은 `clamp01(초 / 90)`, 개선량은 `clamp(가장 최근 진행률 − 가장 오래된 진행률, −0.4, 0.4)`입니다.
- 준비도는 `0.65 × 가중 진행률 + 0.20 × 가중 생존 + 0.35 × 개선량 − 0.15 × 피해 압박`입니다. 피해 압박은 `clamp01(최근 받은 피해 / max(30, 최근 생존 초 × 3))`입니다.
- 다음 도전과 목표의 차이가 1이면 최대 변화폭의 60%, 목표에 도달했거나 초과했으면 100%를 내립니다. 목표까지 더 남은 경우 준비도가 0.27 미만이면 변화폭의 65%를 내리고, 준비도가 0.57을 넘는 패배 뒤에는 변화폭의 `clamp01((준비도 − 0.5) × 3)`만큼 올립니다. 그 외에는 이전 값을 유지합니다.
- 패턴별 취약도는 `clamp01(Σ(평균 피해 × 피격 횟수) / max(1, 총 실행 횟수 × 25))`입니다. 난이도가 낮을수록 취약한 패턴의 등장 가중치 보간 위치를 최대 0.35만큼 낮춥니다. 직전 전경 패턴을 제외한 가중 무작위 추첨을 사용하고, 1페이즈 배경 낙석의 가중치는 반복 주기에 반영합니다.
- HP, 이동·탄환 속도, 패턴 간격, 공격 판정 크기, 피해량, 등장 가중치를 도전 시작에 고정합니다. 보스 HP는 0.45–1.7배, 속도는 0.7–1.3배, 간격은 0.7–1.6배, 공격 판정은 0.7–1.2배, 피해는 0.45–1.6배, 가중치는 0.1–5배의 안전 범위로 제한합니다. 기존 예고와 플레이어의 피격·공격 판정 흐름을 사용하며, 이동 중인 탄환은 생성 시의 파라미터를 유지합니다.

목표는 실제 횟수와 목표의 오차를 줄이는 것입니다. 초기 난이도는 그룹 공통이며 결과에 따라 조정합니다. 목표 전 승리를 허용합니다.

## FastAPI 실행과 배포

Python 3.12 환경에서 `ResearchServer` 폴더를 작업 디렉터리로 사용합니다.

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
Copy-Item .env.example .env
.\.venv\Scripts\python.exe -m uvicorn main:app --host 127.0.0.1 --port 8000 --env-file .env
Invoke-RestMethod http://127.0.0.1:8000/health
```

`RESEARCH_GROUPS`는 그룹 ID, 목표 횟수, 가중치의 JSON 배열입니다. 초기값은 A=3, B=7, C=12회이고 동일 비율입니다. 가중치 1, 2, 1은 25%, 50%, 25%를 뜻합니다. 기존 참가자의 배정은 환경 변수가 바뀌어도 유지됩니다.

서버에는 영속 볼륨의 SQLite 절대 경로를 사용합니다. Uvicorn 인스턴스와 워커는 각각 하나로 운영하고, HTTPS 역방향 프록시 뒤에서 실행합니다. 프록시에서 요청 본문 크기(예: 256 KiB)와 요청 빈도를 제한하고 Authorization 헤더와 본문을 로그에 저장하지 않습니다. `RESEARCH_ADMIN_TOKEN`을 비워 두면 관리자 API는 비활성화됩니다. 값을 설정한 경우 `GET /admin/sync`에 별도 Bearer 토큰을 전달하면 원본과 시트의 동기화 리비전을 확인할 수 있습니다. 관리자 접근은 운영 네트워크로 제한합니다.

운영자는 고지한 보관 기간이 끝나면 SQLite 원본과 분석 시트, 백업 및 내보낸 사본을 정책에 맞춰 정리해야 합니다. 자동 만료 작업은 포함하지 않았습니다. 참가자가 UI에서 제외를 요청하면 서버는 참가·도전·설문 행을 한 트랜잭션에서 제거하고, 재전송에 의한 재등록을 막기 위해 UUID·토큰 해시·요청 시각만 제외 영수증에 보관합니다. 연결이 복구되면 분석 시트에서 해당 행이 사라집니다. 로컬 파일을 분실했을 때는 사후 안내의 연락처로 UUID를 전달하여 담당자가 별도로 처리할 수 있도록 안내합니다.

## Google Sheets 미러

1. Google Cloud에서 Sheets API를 켜고 서비스 계정을 준비합니다. 새 전용 스프레드시트를 만들고 서비스 계정 이메일에 편집 권한을 줍니다. [Google Workspace 서비스 계정 설정](https://developers.google.com/workspace/guides/create-credentials#service-account)
2. `RESEARCH_SPREADSHEET_ID`에 해당 스프레드시트 ID를 넣습니다. 필요한 경우 `GOOGLE_APPLICATION_CREDENTIALS`에 저장소 바깥의 자격 증명 파일 절대 경로를 넣습니다. 호스팅 환경의 ADC를 사용할 수도 있습니다. ID를 비워 두면 SQLite 저장만 수행합니다. [Google Cloud ADC 설정](https://docs.cloud.google.com/docs/authentication/application-default-credentials)
3. 서버가 `Participants`, `Attempts`, `PatternStats`, `Surveys` 탭을 만들고 지정된 열에 일관된 SQLite 스냅샷을 씁니다. 이 탭의 데이터 영역은 수정하지 말고 분석용 수식은 별도 탭에 작성합니다. 요청이 일부 실패해도 SQLite 저장은 완료되며, Sheets 리비전을 기록하지 않아 다음 주기에 재시도합니다. 행을 추가하는 대신 네 탭의 데이터를 한 원자적 배치로 교체해 중복과 제외된 행을 정리합니다. [Google Sheets 원자적 배치 동작](https://developers.google.com/workspace/sheets/api/guides/batch)

## 데이터 사전

| API | JSON 및 SQLite 저장 |
| --- | --- |
| `POST /participants` | UUID, 명시적 동의, UTC 동의 시각, 동의·실험 버전. 기존 서버 형식의 연령대·게임 빈도는 새 참가자에서 `undisclosed`로 전송. 응답은 그룹, 목표 도전 횟수, 배정 버전. SQLite에는 토큰의 SHA-256 해시 저장. |
| `POST /attempts` | UUID, 도전 UUID, 1부터 시작하는 연속 도전 번호, 그룹·목표·버전, UTC 시작·종료, 경과 초, 양측 실제 체력 손실 합계, 두 페이즈의 남은 HP 비율, 패턴 실행·피격·평균 피해, 난이도 스냅샷·적용 시각·이유, 승리 및 종료 사유(`cleared`, `defeat`, `stopped`, `interrupted`). |
| `POST /surveys` | UUID, 설정 버전, UTC 제출 시각, 승리 여부·실제 횟수, 응답 생략 여부, 성취감 별점 한 개. 생략 시 점수 배열은 비어 있음. 기존 여섯 점수 형식도 수신하며, 새 별점은 시트의 `achievement_score`에 기록함. |
| `POST /withdraw` | UUID와 참가자 Bearer 토큰으로 데이터 제외. 성공 204, 같은 요청 반복 가능. |
| `GET /health` | SQLite 접근 가능 시 `{"status":"ok"}`. 참가자 기록은 공개 API로 읽을 수 없음. |

피해량은 과잉 피해를 빼고 실제로 감소한 HP를 합산합니다. 회복 후 다시 입은 피해도 포함합니다. 패턴 평균 피해는 해당 패턴의 받은 피해를 실제 피격 횟수로 나눈 값입니다. 피해가 없으면 0입니다. 패턴 실행 횟수는 스케줄된 시도 횟수이므로, 전투 종료 등으로 중단된 시도를 포함할 수 있습니다. 경과 초는 도전 시작부터 종료 또는 마지막 체크포인트까지의 실시간입니다.

## 검증 방법과 결과

- 세 씬을 연결한 뒤 Unity Editor 컴파일과 실제 로컬 FastAPI를 사용한 Play Mode 테스트를 통과했습니다. 동의·등록 후 게임 진행 씬으로 이동했고, 클리어 후 후기 씬에서 같은 참가자 세션으로 설문과 데이터 제외를 완료했습니다.
- 서버 테스트는 `ResearchServer`에서 `python -m unittest -v`를 실행합니다. FastAPI 통합 테스트 5개가 통과했습니다. 동의·토큰·가중 배정·재시작 시 동일 배정, 중복 제출, 조기 클리어 저장, 설문과 생략, 제외, 모의 Sheets 오류 및 재동기화를 검증했습니다.
- Unity의 **Tools → Boss Arena → Research → Run Study Smoke Test**는 별도 저장 폴더에 테스트 참가자를 생성합니다. 실제 참가 기록이 없는 Editor에서 `BossStudyStart`를 열고, 별도 SQLite DB의 서버를 18741 포트로 시작합니다. `RESEARCH_GROUPS`를 `[{"group":"B","target_attempts":7,"weight":1}]`로 설정합니다. 테스트는 시작 → 진행 → 후기 씬 전환, 세션 유지, 동의 전 차단, UUID 재로드, 페이즈 전환과 1회 클리어, 연결 실패 후 재전송, 설문 및 제외를 확인합니다. 결과는 `Temp/BossStudySmoke.txt`에 기록됩니다. 피해 입력은 재현성을 위해 실제 전투 체력 메서드에 테스트가 직접 전달합니다.
- **Tools → Boss Arena → Research → Validate DDA and Simulate**에서 DDA 방향·변화폭 4개 검증이 통과했습니다. 실력 3단계, 목표 3종에 각 300명씩 시드 315로 모의 실험합니다. `Temp/BossStudySimulation.txt`에 기록된 조합별 MAE는 1.35–9.97회, ±1회 달성률은 0–63.4%, 중도 종료 및 24회 미클리어 비율은 0–31.7%였습니다. 게임 실측 대신 난이도·실력·학습·확률로 결과를 생성하는 모형이며, 참가자 데이터에서의 달성률은 아직 확인하지 않았습니다.
- 실측 집계는 `python metrics.py study.sqlite3`으로 출력합니다. MAE와 ±1회 달성률 분모는 클리어 참가자, 중도 포기율 분모는 종료가 관측된 참가자(클리어 또는 자발적 종료)입니다. 활동이 재개되지 않은 열린 참가자는 따로 표시합니다.
- 실제 Google 서비스 계정과 스프레드시트가 제공되면 운영 배포 전에 테스트 참가자 하나를 등록하고, Sheets에 반영된 뒤 제외 요청이 네 탭에서 사라지는지 확인합니다. 이번 검증은 모의 Sheets REST 응답을 사용했습니다.

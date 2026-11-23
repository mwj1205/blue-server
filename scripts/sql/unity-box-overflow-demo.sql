-- 로컬 테스트 전용. 먼저 unity-box-overflow-demo 계정을 API /register로 가입한다.
-- 기존 보유량·메일 수령량·사용 이력을 초기화하지 않는다.
BEGIN;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM "Players" WHERE "Nickname" = 'unity-box-overflow-demo') THEN
        RAISE EXCEPTION 'Register unity-box-overflow-demo first.';
    END IF;
    IF (SELECT count(*) FROM "ItemTemplates" WHERE "Id" IN (3101,3102,3103,3104) AND "IsActive") <> 4 THEN
        RAISE EXCEPTION 'Synchronize currency box templates first.';
    END IF;
END $$;

INSERT INTO "PlayerItems" ("PlayerId", "ItemTemplateId", "Quantity")
SELECT p."Id", t."Id", 999990 FROM "Players" p CROSS JOIN "ItemTemplates" t
WHERE p."Nickname" = 'unity-box-overflow-demo' AND t."Id" IN (3101,3102,3103,3104)
ON CONFLICT ("PlayerId", "ItemTemplateId") DO NOTHING;

WITH inserted AS (
    INSERT INTO "Mails" ("PlayerId", "SourceType", "SourceId", "Title", "Body", "SentAt", "ExpiresAt")
    SELECT p."Id", 3, 'unity-box-overflow-demo:' || t."Id", 'Unity box overflow ' || t."Id",
           'Claim 9 of 20, leave 11. Use 20 boxes, then claim the remaining 11. Expiry stays unchanged.',
           now(), now() + interval '30 days'
    FROM "Players" p CROSS JOIN "ItemTemplates" t
    WHERE p."Nickname" = 'unity-box-overflow-demo' AND t."Id" IN (3101,3102,3103,3104)
    ON CONFLICT ("PlayerId", "SourceType", "SourceId") DO NOTHING
    RETURNING "Id", "SourceId"
)
INSERT INTO "MailItemAttachments" ("MailId", "ItemTemplateId", "Quantity", "ClaimedQuantity")
SELECT "Id", split_part("SourceId", ':', 2)::int, 20, 0 FROM inserted;
COMMIT;

SELECT p."Id", p."Nickname", p."Gold", p."Gem", i."ItemTemplateId", i."Quantity"
FROM "Players" p JOIN "PlayerItems" i ON i."PlayerId"=p."Id"
WHERE p."Nickname"='unity-box-overflow-demo' ORDER BY i."ItemTemplateId";
SELECT m."Id", m."Title", m."ExpiresAt", a."Quantity", a."ClaimedQuantity"
FROM "Mails" m JOIN "Players" p ON m."PlayerId"=p."Id"
JOIN "MailItemAttachments" a ON a."MailId"=m."Id"
WHERE p."Nickname"='unity-box-overflow-demo' ORDER BY m."Id";

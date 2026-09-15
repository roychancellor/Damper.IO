ALTER TABLE damper.integration
    ALTER COLUMN created_at SET DEFAULT now();

ALTER TABLE damper.integration
    ALTER COLUMN modified_at SET DEFAULT now();
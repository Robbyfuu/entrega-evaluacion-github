"use client";

import { useMemo } from "react";
import type { EvaluationRow, SectionRow } from "@/lib/types";
import { useEvaluations } from "@/hooks/useEvaluations";
import { useSectionLookup } from "@/hooks/useSectionLookup";

interface EvaluationSelectorProps {
  // null = "Control global (todas las secciones)" — legacy blast-radius path.
  selectedEvaluationId: number | null;
  onSelect: (evaluationId: number | null) => void;
}

// Course/section/evaluation picker for the control panel. Selecting a concrete
// evaluation switches the control card to the per-evaluation override path and
// hides the global blast-radius toggle (mixed-client safety, correction 5).
export function EvaluationSelector({
  selectedEvaluationId,
  onSelect,
}: EvaluationSelectorProps) {
  const { rows: evaluations } = useEvaluations();
  const { sectionById, courseById } = useSectionLookup();

  // Label each evaluation with its course / section / title so the teacher
  // controls the right one without ambiguity.
  const labelFor = useMemo(() => {
    return (evaluation: EvaluationRow): string => {
      const sec: SectionRow | undefined = sectionById.get(evaluation.section_id);
      const courseCode = sec ? courseById.get(sec.course_id)?.code ?? "?" : "?";
      const sectionCode = sec?.code ?? "?";
      const numberPart = evaluation.number != null ? ` (#${evaluation.number})` : "";
      return `${courseCode} / ${sectionCode} · ${evaluation.title}${numberPart}`;
    };
  }, [sectionById, courseById]);

  return (
    <div className="field" style={{ flex: "1 1 320px", maxWidth: 480 }}>
      <label htmlFor="controlEvalSelect">Evaluación a controlar</label>
      <select
        id="controlEvalSelect"
        value={selectedEvaluationId == null ? "" : String(selectedEvaluationId)}
        onChange={(e) =>
          onSelect(e.target.value === "" ? null : Number(e.target.value))
        }
      >
        <option value="">Control global (afecta a TODAS las secciones)</option>
        {evaluations.map((ev) => (
          <option key={ev.id} value={ev.id}>
            {labelFor(ev)}
          </option>
        ))}
      </select>
    </div>
  );
}

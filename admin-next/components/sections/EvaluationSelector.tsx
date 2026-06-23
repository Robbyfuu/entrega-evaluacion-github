"use client";

import { useMemo } from "react";
import type { EvaluationRow, SectionRow } from "@/lib/types";
import { useEvaluations } from "@/hooks/useEvaluations";
import { useSectionLookup } from "@/hooks/useSectionLookup";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

interface EvaluationSelectorProps {
  // null = "Control global (todas las secciones)" — legacy blast-radius path.
  selectedEvaluationId: number | null;
  onSelect: (evaluationId: number | null) => void;
}

// Sentinel value for the radix Select item that maps back to null (global).
const GLOBAL_VALUE = "__global__";

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
    <div className="flex w-full max-w-[480px] flex-col gap-1.5">
      <Label htmlFor="controlEvalSelect">Evaluación a controlar</Label>
      <Select
        value={
          selectedEvaluationId == null
            ? GLOBAL_VALUE
            : String(selectedEvaluationId)
        }
        onValueChange={(value) =>
          onSelect(value === GLOBAL_VALUE ? null : Number(value))
        }
      >
        <SelectTrigger id="controlEvalSelect" className="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={GLOBAL_VALUE}>
            Control global (afecta a TODAS las secciones)
          </SelectItem>
          {evaluations.map((ev) => (
            <SelectItem key={ev.id} value={String(ev.id)}>
              {labelFor(ev)}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}

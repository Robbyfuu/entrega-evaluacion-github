"use client";

import { AlertTriangle } from "lucide-react";
import type { OnlineClientRow } from "@/lib/types";
import { isSuspicious } from "@/lib/suspicious";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { ScrollArea } from "@/components/ui/scroll-area";
import { cn } from "@/lib/utils";

interface ProcessModalProps {
  client: OnlineClientRow | null;
  onClose: () => void;
}

// Modal listing the open processes of a single PC. Replicates #processModal.
export function ProcessModal({ client, onClose }: ProcessModalProps) {
  const procs = client && Array.isArray(client.processes) ? client.processes : [];

  return (
    <Dialog open={!!client} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Programas abiertos en {client?.pc_name}</DialogTitle>
          <DialogDescription>@{client?.github_username || "?"}</DialogDescription>
        </DialogHeader>
        <ScrollArea className="max-h-[60vh]">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Proceso</TableHead>
                <TableHead>Título de ventana</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {procs.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={2} className="text-center text-muted-foreground">
                    Sin procesos con ventana visible.
                  </TableCell>
                </TableRow>
              ) : (
                procs.map((p, i) => {
                  const susp = isSuspicious(p.name);
                  return (
                    <TableRow key={i} className={susp ? "bg-destructive/10" : undefined}>
                      <TableCell
                        className={cn(susp && "font-semibold text-destructive")}
                      >
                        <span className="inline-flex items-center gap-1.5">
                          {susp ? <AlertTriangle className="size-3.5" /> : null}
                          {p.name || "-"}
                        </span>
                      </TableCell>
                      <TableCell>{p.title || "-"}</TableCell>
                    </TableRow>
                  );
                })
              )}
            </TableBody>
          </Table>
        </ScrollArea>
      </DialogContent>
    </Dialog>
  );
}

// @ts-nocheck
"use client";
import { Card, CardContent } from "@/components/ui/card";

import { ChartConfig, ChartContainer, ChartTooltip, ChartTooltipContent } from "@/components/ui/chart";
import { Pie, PieChart } from "recharts";
import { useDashboard } from "../dashboard-state";
import { ChartHeader } from "./chart-header";
import { computeModelData } from "./common";
import { ListItems } from "./language";

export const Model = () => {
  const { filteredData } = useDashboard();
  const data = computeModelData(filteredData);

  return (
    <Card className="col-span-4 md:col-span-2">
      <ChartHeader
        title="Model"
        description="Percentage of active users per model"
      />
      <CardContent>
        <div className="w-full h-full flex flex-col gap-4 ">
          <div>
            <ChartContainer
              config={chartConfig}
              className="mx-auto aspect-square max-h-[250px]"
            >
              <PieChart>
                <ChartTooltip content={<ChartTooltipContent hideLabel />} />
                <Pie
                  paddingAngle={1}
                  data={data}
                  dataKey="value"
                  nameKey="name"
                  innerRadius={40}
                  cornerRadius={5}
                />
              </PieChart>
            </ChartContainer>
          </div>
          <div className="flex flex-col gap-4 text-sm flex-wrap">
            <ListItems items={data} />
          </div>
        </div>
      </CardContent>
    </Card>
  );
};

const chartConfig = {} satisfies ChartConfig;

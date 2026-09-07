import 'dart:io';

import 'package:appflowy_board/appflowy_board.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:haven_boards_appflowy_poc/main.dart';

void main() {
  test('AppFlowy controller reports and applies Haven-compatible reorder', () {
    String? movedGroup;
    int? movedFrom;
    int? movedTo;
    final controller = AppFlowyBoardController(
      onMoveGroupItem: (groupId, fromIndex, toIndex) {
        movedGroup = groupId;
        movedFrom = fromIndex;
        movedTo = toIndex;
      },
    );
    addTearDown(controller.dispose);

    controller.addGroup(
      AppFlowyGroupData(
        id: 'todo',
        name: 'To do',
        items: [
          HavenBoardItem('a', 'A'),
          HavenBoardItem('b', 'B'),
          HavenBoardItem('c', 'C'),
        ],
      ),
    );

    controller.moveGroupItem('todo', 0, 1);

    expect(movedGroup, 'todo');
    expect(movedFrom, 0);
    expect(movedTo, 1);
    expect(
      controller.getGroupController('todo')!.items.map((item) => item.id),
      ['b', 'a', 'c'],
    );
  });

  testWidgets('every card exposes non-drag movement controls', (tester) async {
    final originalDirectory = Directory.current;
    final tempDirectory = await Directory.systemTemp.createTemp('haven-boards-appflowy-test-');
    Directory.current = tempDirectory.path;

    try {
      await tester.pumpWidget(const HavenBoardsPocApp());
      // AppFlowy Board may keep scroll/animation machinery active, so use bounded
      // pumps rather than pumpAndSettle (which can wait indefinitely for quiescence).
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 150));

      expect(find.byTooltip('Move card up'), findsWidgets);
      expect(find.byTooltip('Move card down'), findsWidgets);
      expect(find.byTooltip('Move card to previous group'), findsWidgets);
      expect(find.byTooltip('Move card to next group'), findsWidgets);

      final firstCard = find.ancestor(
        of: find.text('First task'),
        matching: find.byType(Card),
      );
      expect(firstCard, findsOneWidget);
      expect(
        find.descendant(of: firstCard, matching: find.byType(Semantics)),
        findsWidgets,
      );
    } finally {
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pump();
      Directory.current = originalDirectory.path;
      if (await tempDirectory.exists()) {
        await tempDirectory.delete(recursive: true);
      }
    }
  });
}
